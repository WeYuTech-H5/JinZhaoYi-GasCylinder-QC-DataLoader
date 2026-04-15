namespace JinZhaoYi.GasQcDataLoader.Tests;

internal static class QuantFixtures
{
    public const string Std0947 =
        """
                                                Quantitation Report    (Not Reviewed)

          Data Path : D:\data\
          Data File : 0001.D
          Acq On    : 19 Nov 2025  09:47
          Operator  :
          Sample    : Sample 1
          Misc      : port 1  903  872>  #20251030001
          ALS Vial  : 1   Sample Multiplier: 1

          Quant Time: Nov 19 09:56:40 2025
          Quant Method : D:\GCMSD\Method\TSMC_15min.M
          Quant Title  :
          QLast Update : Tue Jul 22 10:40:54 2025
          Response via : Initial Calibration

                  Compound                   R.T. QIon  Response  Conc Units Dev(Min)
           --------------------------------------------------------------------------

           Target Compounds                                                   Qvalue
             1) Freon114                    1.594   85  8099970    82.89 ppb       99
             2) IPA                         2.164   45  4534252   143.41 ppb       87
             3) Acetone                     2.134   58  1204986    68.36 ppb       98
             5) 1,1-Dichloroethene          2.397   61  4545347    63.95 ppb      100
             6) Freon113                    2.462  101  6725010    76.48 ppb       89
             7) Methylene Chloride          2.587   49  2513092    45.03 ppb       86
             8) CNF                         2.718   69 33717521    75.22 ppb       80
             9) Cyclopentane                2.946   42  3386399    35.08 ppb       86
            10) 1,1-Dichloroethane          3.118   63  4870198    62.38 ppb  #    74
            11) 2-Butanone                  3.285   43  4766994    63.06 ppb       75
            13) Ethyl Acetate               3.517   43  5750289    65.28 ppb       93
            14) cis-1,2-Dichloroethene      3.491   61  4757073    64.61 ppb       95
            15) Freon20                     3.712   83  7221439    85.86 ppb       97
            16) 1,1,1-Trichloroethane       4.029   97  9141323   102.31 ppb       93
            17) 1,2-Dichloroethane          4.111   62  5584769    95.20 ppb  #    68
            18) Benzene                     4.240   78  8463829    72.44 ppb       85
            19) Carbon Tetrachloride        4.258  117 10660461   117.15 ppb       98
            21) Trichloroethylene           4.671  130  4565853    73.90 ppb       96
            22) 1,2-Dichloropropane         4.689   63  3181651    59.67 ppb  #    72
            23) cis-1,3-Dichloropropene     5.023   75  6371658    89.40 ppb  #    39
            24) trans-1,3-Dichloropropene   5.232   75  5594561    88.59 ppb  #    45
            25) Toluene                     5.266   91 12197808    81.53 ppb       97
            26) 1,1,2-Trichloroethane       5.324   97  4264208    80.99 ppb       91
            27) Tetrachloroethylene         5.571  166  7282060    86.75 ppb       96
            28) 1,2-Dibromoethane           5.609  107  7385077    93.28 ppb  #    46
            30) ChloroBenzene.              5.845  112 11315788    90.51 ppb       89
            31) EthylBenzene                5.911   91 18480992    97.15 ppb       93
            32) m/p-Xylene                  5.962   91 15143151   103.01 ppb       97
            33) Styrene                     6.093  104 11290060    97.88 ppb  #    88
            34) o-Xylene                    6.103   91 15507805   102.83 ppb       98
            35) 1,1,2,2-Tetrachloroethane   6.222   83  9766365    97.06 ppb       98
            36) 1,3,5-TMB                   6.462  105 18751990   101.43 ppb       99
            37) 1,2,4-TMB                   6.591  105 18950847   106.68 ppb       98
            38) 1,3-DCB                     6.686  146 12536283    89.44 ppb       99
            39) 1,4-DCB                     6.721  146 12973314   101.58 ppb       99
            40) 1,2-DCB                     6.818  146 11941904    88.17 ppb       99
            41) 1,2,4-TCB                   7.365  180 14201438   101.06 ppb       99
            42) HCBD                        7.449  225 12843868   104.13 ppb       99

           SemiQuant Compounds - Not Calibrated on this Instrument
            29) Chlorobenzene-D5            6.591  117   711829   No Calib   #
           --------------------------------------------------------------------------
        """;
}
