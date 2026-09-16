namespace MadWizard.Desomnia.Processes.Tests
{
    // TODO Move to DesomniaLaunchDaemon
    //public class ProcessManagerConfigTests
    //{
    //    [Fact]
    //    public void MeasureGPU_DefaultsToAutomatic()
    //    {
    //        Assert.Equal(GraphicsMeasurementMode.Automatic, new ProcessManagerConfig().MeasureGPU);
    //    }

    //    [Theory]
    //    [InlineData("automatic", GraphicsMeasurementMode.Automatic)]
    //    [InlineData("process", GraphicsMeasurementMode.Process)]
    //    [InlineData("coalition", GraphicsMeasurementMode.Coalition)]
    //    public void MeasureGPU_BindsCaseInsensitively(string configured, GraphicsMeasurementMode expected)
    //    {
    //        var configuration = new ConfigurationBuilder()
    //            .AddInMemoryCollection(new Dictionary<string, string?> { ["measureGPU"] = configured })
    //            .Build();

    //        var result = StrictConfigurationBinder.Get<ProcessManagerConfig>(configuration)!;

    //        Assert.Equal(expected, result.MeasureGPU);
    //    }
    //}
}
