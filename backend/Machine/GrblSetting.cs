namespace Backend.Machine;

/// <summary>One GRBL configuration entry from the $$ report.</summary>
public sealed class GrblSetting
{
    public int Id { get; set; }
    public string Value { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Known GRBL $$ parameter descriptions (GRBL 1.1 / grblHAL common).</summary>
    public static string DescribeId(int id) => id switch
    {
        0   => "Step pulse time (µs)",
        1   => "Step idle delay (ms)",
        2   => "Step port invert mask",
        3   => "Direction port invert mask",
        4   => "Step enable invert",
        5   => "Limit pins invert",
        6   => "Probe pin invert",
        10  => "Status report mask",
        11  => "Junction deviation (mm)",
        12  => "Arc tolerance (mm)",
        13  => "Report inches",
        20  => "Soft limits enable",
        21  => "Hard limits enable",
        22  => "Homing cycle enable",
        23  => "Homing direction invert mask",
        24  => "Homing feed (mm/min)",
        25  => "Homing seek (mm/min)",
        26  => "Homing debounce delay (ms)",
        27  => "Homing pull-off (mm)",
        30  => "Max spindle speed (RPM)",
        31  => "Min spindle speed (RPM)",
        32  => "Laser mode enable",
        100 => "X steps/mm",
        101 => "Y steps/mm",
        102 => "Z steps/mm",
        110 => "X max rate (mm/min)",
        111 => "Y max rate (mm/min)",
        112 => "Z max rate (mm/min)",
        120 => "X acceleration (mm/s²)",
        121 => "Y acceleration (mm/s²)",
        122 => "Z acceleration (mm/s²)",
        130 => "X max travel (mm)",
        131 => "Y max travel (mm)",
        132 => "Z max travel (mm)",
        _   => $"Setting ${id}",
    };
}
