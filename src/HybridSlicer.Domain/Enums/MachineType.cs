namespace HybridSlicer.Domain.Enums;

public enum MachineType
{
    FDM = 0,
    CNC = 1,
    Hybrid = 2,
    MSLA = 3,
    DLP = 4,
}

public enum PrinterOrientation
{
    BottomUp = 0,
    TopDown = 1,
}

public enum AntiAliasingLevel
{
    None = 1,
    X2 = 2,
    X4 = 4,
    X8 = 8,
}
