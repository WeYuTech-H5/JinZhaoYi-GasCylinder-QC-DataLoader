from __future__ import annotations

import argparse
import json
import subprocess
from pathlib import Path

from PIL import Image, ImageChops
from pypdf import PdfReader


def find_header(page: Image.Image) -> tuple[tuple[int, int, int, int], tuple[int, int]]:
    image = page.convert("RGB")
    width, height = image.size
    pixels = image.load()

    colored = []
    for y in range(0, min(height // 5, 700)):
        for x in range(0, min(width // 2, 1000)):
            red, green, blue = pixels[x, y]
            if max(red, green, blue) - min(red, green, blue) > 55 and min(red, green, blue) < 210:
                colored.append((x, y))

    if not colored:
        raise RuntimeError("Could not locate the colored Excel logo.")

    logo_left = min(point[0] for point in colored)
    logo_top = min(point[1] for point in colored)
    search_left = max(0, logo_left - 5)
    search_top = max(0, logo_top - 12)
    search_right = min(width, logo_left + 720)
    search_bottom = min(height, logo_top + 115)

    ink = []
    for y in range(search_top, search_bottom):
        for x in range(search_left, search_right):
            if min(pixels[x, y]) < 235:
                ink.append((x, y))

    if not ink:
        raise RuntimeError("Could not locate header ink around the Excel logo.")

    header = (
        min(point[0] for point in ink),
        min(point[1] for point in ink),
        max(point[0] for point in ink) + 1,
        max(point[1] for point in ink) + 1,
    )

    row_counts = []
    for y in range(header[3] + 18, min(height, header[3] + 110)):
        count = sum(
            1
            for x in range(max(0, header[0] - 80), min(width, header[2] + 180))
            if min(pixels[x, y]) < 210
        )
        if count > 10:
            row_counts.append(y)

    if not row_counts:
        raise RuntimeError("Could not locate the NF-SEMI baseline below the header.")

    baseline_top = row_counts[0]
    baseline_pixels = []
    for y in range(baseline_top, min(height, baseline_top + 35)):
        for x in range(max(0, header[0] - 80), min(width, header[2] + 180)):
            if min(pixels[x, y]) < 210:
                baseline_pixels.append((x, y))

    baseline_left = min(point[0] for point in baseline_pixels)
    return header, (baseline_left, baseline_top)


def compare(reference_path: Path, actual_path: Path, output_dir: Path) -> dict[str, float | int | list[int]]:
    reference_page = Image.open(reference_path).convert("RGB")
    actual_page = Image.open(actual_path).convert("RGB")
    reference_box, reference_baseline = find_header(reference_page)
    actual_box, actual_baseline = find_header(actual_page)

    reference_header = reference_page.crop(reference_box)
    actual_header = actual_page.crop(actual_box)
    output_dir.mkdir(parents=True, exist_ok=True)
    reference_header.save(output_dir / "excel-reference-header.png")
    actual_header.save(output_dir / "libreoffice-header.png")

    size_delta = (
        abs(reference_header.width - actual_header.width),
        abs(reference_header.height - actual_header.height),
    )
    normalized_actual = actual_header.resize(reference_header.size, Image.Resampling.LANCZOS)
    difference = ImageChops.difference(reference_header, normalized_actual)
    difference.point(lambda value: min(255, value * 4)).save(output_dir / "header-difference.png")
    Image.blend(reference_header, normalized_actual, 0.5).save(output_dir / "header-overlay.png")

    reference_bytes = reference_header.tobytes()
    actual_bytes = normalized_actual.tobytes()
    absolute_error = [abs(left - right) for left, right in zip(reference_bytes, actual_bytes)]
    mae = sum(absolute_error) / len(absolute_error)
    changed_pixels = 0
    for index in range(0, len(absolute_error), 3):
        if max(absolute_error[index:index + 3]) > 32:
            changed_pixels += 1
    changed_ratio = changed_pixels / (reference_header.width * reference_header.height)

    reference_relative = (
        reference_box[0] - reference_baseline[0],
        reference_box[1] - reference_baseline[1],
    )
    actual_relative = (
        actual_box[0] - actual_baseline[0],
        actual_box[1] - actual_baseline[1],
    )
    position_delta = (
        abs(reference_relative[0] - actual_relative[0]),
        abs(reference_relative[1] - actual_relative[1]),
    )

    return {
        "reference_box": list(reference_box),
        "actual_box": list(actual_box),
        "reference_size": list(reference_header.size),
        "actual_size": list(actual_header.size),
        "size_delta": list(size_delta),
        "reference_relative_to_nf_semi": list(reference_relative),
        "actual_relative_to_nf_semi": list(actual_relative),
        "position_delta": list(position_delta),
        "mean_absolute_error": mae,
        "changed_pixel_ratio": changed_ratio,
    }


def render_pdf_page(
    pdftoppm_path: Path,
    pdf_path: Path,
    output_dir: Path,
    dpi: int,
) -> Path:
    output_prefix = output_dir / f"libreoffice-page-{dpi}dpi"
    subprocess.run(
        [
            str(pdftoppm_path),
            "-f",
            "1",
            "-singlefile",
            "-r",
            str(dpi),
            "-png",
            str(pdf_path),
            str(output_prefix),
        ],
        check=True,
    )
    return output_prefix.with_suffix(".png")


def grouped_runs(values: list[int]) -> list[list[int]]:
    runs: list[list[int]] = []
    for value in values:
        if not runs or value > runs[-1][-1] + 1:
            runs.append([])
        runs[-1].append(value)
    return runs


def verify_crop_marks(page_path: Path) -> dict[str, int | list[int]]:
    page = Image.open(page_path).convert("L")
    pixels = page.load()
    width, height = page.size

    left_zone_start = round(width * 0.043)
    left_zone_end = round(width * 0.057)
    top_zone_start = round(height * 0.018)
    top_zone_end = round(height * 0.034)

    y_runs = grouped_runs(
        [
            y
            for y in range(height)
            if any(pixels[x, y] < 150 for x in range(left_zone_start, left_zone_end))
        ]
    )
    x_runs = grouped_runs(
        [
            x
            for x in range(width)
            if any(pixels[x, y] < 150 for y in range(top_zone_start, top_zone_end))
        ]
    )
    crop_y = [
        round((run[0] + run[-1]) / 2)
        for run in y_runs
        if len(run) <= 8
    ]
    crop_x = [
        round((run[0] + run[-1]) / 2)
        for run in x_runs
        if len(run) <= 8
    ]

    intersections = []
    for y in crop_y:
        for x in crop_x:
            ink_count = sum(
                pixels[check_x, check_y] < 180
                for check_y in range(max(0, y - 6), min(height, y + 7))
                for check_x in range(max(0, x - 5), min(width, x + 6))
            )
            intersections.append(ink_count)

    present = sum(ink_count > 0 for ink_count in intersections)
    if len(crop_x) != 4 or len(crop_y) != 4 or present != 16:
        raise RuntimeError(
            f"Expected 16 crop-mark intersections, found x={crop_x}, y={crop_y}, present={present}."
        )

    return {
        "crop_x": crop_x,
        "crop_y": crop_y,
        "intersection_count": len(intersections),
        "present_intersection_count": present,
        "intersection_ink_counts": intersections,
    }


def verify_pdf_fonts(pdf_path: Path) -> dict[str, list[str]]:
    font_names = sorted(
        {
            str(font.get_object().get("/BaseFont", ""))
            for page in PdfReader(pdf_path).pages
            for font in page["/Resources"].get("/Font", {}).values()
        }
    )
    required_fonts = [
        "MicrosoftJhengHeiUIRegular",
        "MicrosoftJhengHeiUIBold",
    ]
    missing_fonts = [
        required
        for required in required_fonts
        if not any(required in actual for actual in font_names)
    ]
    if missing_fonts:
        raise RuntimeError(
            f"Missing required PDF fonts {missing_fonts}; actual fonts: {font_names}."
        )
    return {
        "required_fonts": required_fonts,
        "actual_fonts": font_names,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--reference-page", type=Path)
    parser.add_argument("--actual-pdf", type=Path, required=True)
    parser.add_argument("--pdftoppm", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--verify-crop-marks", action="store_true")
    parser.add_argument("--verify-fonts", action="store_true")
    parser.add_argument("--max-mae", type=float, default=8.0)
    parser.add_argument("--max-changed-ratio", type=float, default=0.08)
    parser.add_argument("--max-size-delta", type=int, default=2)
    parser.add_argument("--max-position-delta", type=int, default=2)
    args = parser.parse_args()

    args.output_dir.mkdir(parents=True, exist_ok=True)
    if args.reference_page is None and not args.verify_crop_marks and not args.verify_fonts:
        raise SystemExit("No PDF verification was requested.")

    result: dict[str, object] = {}
    if args.reference_page is not None:
        actual_page = render_pdf_page(args.pdftoppm, args.actual_pdf, args.output_dir, 300)
        result["header"] = compare(args.reference_page, actual_page, args.output_dir)
    if args.verify_crop_marks:
        crop_mark_page = render_pdf_page(args.pdftoppm, args.actual_pdf, args.output_dir, 150)
        result["crop_marks"] = verify_crop_marks(crop_mark_page)
    if args.verify_fonts:
        result["fonts"] = verify_pdf_fonts(args.actual_pdf)

    (args.output_dir / "pdf-verification.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(json.dumps(result, ensure_ascii=False))

    failures = []
    header_result = result.get("header")
    if isinstance(header_result, dict):
        if header_result["mean_absolute_error"] > args.max_mae:
            failures.append(
                f"MAE {header_result['mean_absolute_error']:.4f} > {args.max_mae:.4f}"
            )
        if header_result["changed_pixel_ratio"] > args.max_changed_ratio:
            failures.append(
                "changed ratio "
                f"{header_result['changed_pixel_ratio']:.4%} > {args.max_changed_ratio:.4%}"
            )
        if max(header_result["size_delta"]) > args.max_size_delta:
            failures.append(
                f"size delta {header_result['size_delta']} > {args.max_size_delta}"
            )
        if max(header_result["position_delta"]) > args.max_position_delta:
            failures.append(
                f"position delta {header_result['position_delta']} > {args.max_position_delta}"
            )

    if failures:
        raise SystemExit("; ".join(failures))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
