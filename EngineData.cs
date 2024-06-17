//Example class to poll the TN web server and extract knock/engine data
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;

public class EngineData
{
    public int RPM { get; set; }
    public double MAP { get; set; }
    public int VolumeLeft { get; set; }
    public int VolumeRight { get; set; }
    public int Threshold { get; set; }
    public int KnockedLeft { get; set; }
    public int KnockedRight { get; set; }
    public int[] CylinderKnockCounts { get; set; }

    public static async Task<EngineData> FromJsonAsync(string jsonString)
    {
        return await JsonSerializer.DeserializeAsync<EngineData>(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonString)));
    }

    private static readonly HttpClient client = new HttpClient();
    private static Timer timer;

    public EngineData()
    {
        // Initialize timer to poll every 10ms
        timer = new Timer(10);
        timer.Elapsed += async (sender, e) => await PollServerAsync();
        timer.Start();
    }

    private async Task PollServerAsync()
    {
        try
        {
            HttpResponseMessage response = await client.GetAsync("http://localhost:80");
            response.EnsureSuccessStatusCode();
            string responseBody = await response.Content.ReadAsStringAsync();
            EngineData engineData = await FromJsonAsync(responseBody);

            // Now you have the engineData object populated
            Console.WriteLine($"RPM: {engineData.RPM}");
            Console.WriteLine($"MAP: {engineData.MAP}");
            Console.WriteLine($"Volume Left: {engineData.VolumeLeft}");
            Console.WriteLine($"Volume Right: {engineData.VolumeRight}");
            Console.WriteLine($"Threshold: {engineData.Threshold}");
            Console.WriteLine($"Knocked Left: {engineData.KnockedLeft}");
            Console.WriteLine($"Knocked Right: {engineData.KnockedRight}");
            Console.WriteLine("Cylinder Knock Counts: " + string.Join(", ", engineData.CylinderKnockCounts));
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine("\nException Caught!");
            Console.WriteLine("Message: {0} ", e.Message);
        }
    }
}
