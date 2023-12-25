using csharp_ef_webapi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Protocol;

namespace csharp_ef_webapi.Services;
public class DotaWebApiService
{
    private readonly string _baseApiUrl;
    private readonly string _steamKey;
    private readonly HttpClient _httpClient;
    private readonly Timer _timer;
    private readonly ILogger<DotaWebApiService> _logger;
    private readonly AghanimsWagerContext _dbContext;
    IConfiguration _configuration;

    public DotaWebApiService(ILogger<DotaWebApiService> logger, IConfiguration configuration, AghanimsWagerContext dbContext)
    {
        _logger = logger;
        _configuration = configuration;
        _dbContext = dbContext;

        _baseApiUrl = _configuration.GetSection("DotaWebApi").GetValue<string>("BaseUrl");
        _steamKey = Environment.GetEnvironmentVariable("STEAM_KEY") ?? "";

        _httpClient = new HttpClient();
        _logger.LogInformation("Dota WebApi Service started");
        _timer = new Timer(GetDataCallback, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }

    private async void GetDataCallback(object? state)
    {
        try
        {
            List<League> leagues = _dbContext.Leagues.Where(l => l.isActive).ToList();
            foreach (League league in leagues)
            {
                _logger.LogInformation($"Fetching league matches for league ID {league.id}");
                List<MatchHistory> matches = await GetMatchHistoryAsync(league.id);
                foreach (MatchHistory match in matches)
                {
                    if (_dbContext.MatchHistory.FirstOrDefault(m => m.matchId == match.matchId) == null)
                    {
                        match.leagueId = league.id;
                        _dbContext.MatchHistory.Add(match);
                    }
                }
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            // Handle exceptions here
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    private async Task<List<MatchHistory>> GetMatchHistoryAsync(long leagueId)
    {
        List<MatchHistory> matches = new List<MatchHistory>();
        bool endOfLeague = false;
        long? startMatchId = null;
        while (!endOfLeague)
        {
            HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Get, _baseApiUrl);
            UriBuilder uriBuilder = new UriBuilder($"{_baseApiUrl}/GetMatchHistory/v1");
            uriBuilder.Query = $"key={_steamKey}&league_id={leagueId}&matches_requested=100";

            if (startMatchId.HasValue)
            {
                uriBuilder.Query += $"&start_at_match_id={startMatchId}";
            }

            httpRequest.RequestUri = uriBuilder.Uri;

            HttpResponseMessage response = await _httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            JToken responseRawJToken = JToken.Parse(await response.Content.ReadAsStringAsync());

            // Read and deserialize the matches from the json response
            JToken responseObject = responseRawJToken["result"] ?? "{}";
            int resultsRemaining = (responseObject["results_remaining"] ?? 0).Value<int>();
            JToken matchesJson = responseObject["matches"] ?? "[]";

            List<MatchHistory> matchResponse = JsonConvert.DeserializeObject<List<MatchHistory>>(matchesJson.ToString()) ?? new List<MatchHistory>();

            matches.AddRange(matchResponse.Where(m => m.matchId != startMatchId));

            if (resultsRemaining > 0)
            {
                // Response returns latest matches first
                startMatchId = matchResponse.OrderByDescending(m => m.matchId).Last().matchId;
            }
            else
            {
                endOfLeague = true;
            }
        }

        return matches;
    }
}