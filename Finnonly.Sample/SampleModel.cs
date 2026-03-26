using Finnonly.Avalonia;

namespace Finnonly.Sample;

[CopyTo]
[Compare]
public partial class SampleModel
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public double Score { get; set; }
    public bool IsActive { get; set; }
}
