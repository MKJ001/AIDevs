#:package CsvHelper@33.0.1

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CsvHelper;
using CsvHelper.Configuration;

var openRouterToken = Environment.GetEnvironmentVariable("OPENROUTER_TOKEN")!;
var apiKey = Environment.GetEnvironmentVariable("AIDEVS_API_KEY")!;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

using var reader = new StreamReader("people.csv");
using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
{
    PrepareHeaderForMatch = args => args.Header.ToLower()
});

var people = csv.GetRecords<Person>().ToArray();
var suitablePeople = people.Where(item => item.Gender == "M" && item.Age >= 20 && item.Age <= 40 && item.BirthPlace == "Grudziądz").ToArray();

using var openRouterClient = new HttpClient()
{
    BaseAddress = new Uri("https://openrouter.ai/api/v1")
};
openRouterClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openRouterToken);

var tagValues = new[] { "IT", "transport", "edukacja", "medycyna", "praca z ludźmi", "praca z pojazdami", "praca fizyczna" };
var jobsList = string.Join("\n", suitablePeople.Select((p, i) => $"{i}: {p.Job}"));

var payload = new
{
    model = "openai/gpt-4.1-mini",
    messages = new[]
    {
        new { role = "user", content = $"Przypisz tagi do każdego stanowiska pracy. Zwróć tablicę wyników w tej samej kolejności.\n\n{jobsList}" }
    },
    response_format = new
    {
        type = "json_schema",
        json_schema = new
        {
            name = "people_tags",
            strict = true,
            schema = new
            {
                type = "object",
                properties = new
                {
                    results = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                tags = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "string",
                                        @enum = tagValues
                                    }
                                }
                            },
                            required = new[] { "tags" },
                            additionalProperties = false
                        }
                    }
                },
                required = new[] { "results" },
                additionalProperties = false
            }
        }
    }
};

var json = JsonSerializer.Serialize(payload, jsonOptions);
var content = new StringContent(json, Encoding.UTF8, "application/json");
var response = await openRouterClient.PostAsync("/chat/completions", content);
var body = await response.Content.ReadAsStringAsync();

var completion = JsonSerializer.Deserialize<JsonElement>(body, jsonOptions);
var messageContent = completion.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!;
var results = JsonSerializer.Deserialize<JsonElement>(messageContent, jsonOptions).GetProperty("results");

for (var i = 0; i < suitablePeople.Length; i++)
{
    var tags = results[i].GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray();
    suitablePeople[i] = suitablePeople[i] with { Tags = tags };
}

var transportPeople = suitablePeople
    .Where(p => p.Tags.Contains("transport"))
    .Select(p => new
    {
        name = p.Name,
        surname = p.Surname,
        gender = p.Gender,
        born = p.BirthDate.Year,
        city = p.BirthPlace,
        tags = p.Tags
    })
    .ToArray();

var verifyPayload = new
{
    apikey = apiKey,
    task = "people",
    answer = transportPeople
};

using var aiDevsClient = new HttpClient();
var verifyJson = JsonSerializer.Serialize(verifyPayload, jsonOptions);
var verifyContent = new StringContent(verifyJson, Encoding.UTF8, "application/json");
var verifyResponse = await aiDevsClient.PostAsync("https://hub.ag3nts.org/verify", verifyContent);
var verifyBody = await verifyResponse.Content.ReadAsStringAsync();

Console.WriteLine("Result: ");
Console.WriteLine(verifyBody);

record Person
{
    public string Name { get; init; } = "";
    public string Surname { get; init; } = "";
    public string Gender { get; init; } = "";
    public DateOnly BirthDate { get; init; }
    public string BirthPlace { get; init; } = "";
    public string BirthCountry { get; init; } = "";
    public string Job { get; init; } = "";
    public string[] Tags { get; init; } = [];

    public int Age
    {
        get
        {
            var currentDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var years = currentDate.Year - BirthDate.Year;

            if (currentDate < BirthDate.AddYears(years))
                years--;

            return years;
        }
    }
}
