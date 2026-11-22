namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Samples machine-wide CPU utilization (all processes) as a fraction in <c>0..1</c>.
/// </summary>
public interface ICpuLoadSampler
{
    double Sample();
}
