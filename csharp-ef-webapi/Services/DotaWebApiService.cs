using System.Diagnostics;
using csharp_ef_webapi.Models;
using Microsoft.EntityFrameworkCore.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace csharp_ef_webapi.Services;
public class DotaWebApiService
{
    private readonly string _baseApiUrl;
    private readonly string _steamKey;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DotaWebApiService> _logger;
    private readonly AghanimsWagerContext _dbContext;
    private static readonly SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1); // Serialized tasks nothing concurrent
    private readonly Stopwatch stopwatch = new Stopwatch();
    private static readonly TimeSpan delayBetweenRequests = TimeSpan.FromSeconds(1.0 / 2); // 2 requests per second
    IConfiguration _configuration;

    public DotaWebApiService(ILogger<DotaWebApiService> logger, IConfiguration configuration, AghanimsWagerContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
        _configuration = configuration;

        _baseApiUrl = _configuration.GetSection("DotaWebApi").GetValue<string>("BaseUrl");
        _steamKey = Environment.GetEnvironmentVariable("STEAM_KEY") ?? "";

        _httpClient = new HttpClient();
        _logger.LogInformation("Dota WebApi Service started");
        // We're running this once a day because the live games should get us the updates
        new Timer(GetLeagueHistoryDataCallback, null, TimeSpan.Zero, TimeSpan.FromDays(1));
    }

    private async void GetLeagueHistoryDataCallback(object? state)
    {
        try
        {
            // Get all the Match Histories for the leagues first
            List<League> leagues = _dbContext.Leagues.Where(l => l.isActive).ToList();
            _logger.LogInformation($"Fetching league matches for {leagues.Count()} leagues");

            List<Task<List<MatchHistory>>> fetchMatchHistoryTasks = new List<Task<List<MatchHistory>>>();
            stopwatch.Start();

            foreach (League league in leagues)
            {
                fetchMatchHistoryTasks.Add(GetMatchHistoryAsync(league.id));
            }

            await Task.WhenAll(fetchMatchHistoryTasks);

            foreach (MatchHistory match in fetchMatchHistoryTasks.SelectMany(t => t.Result).ToList())
            {
                if (_dbContext.MatchHistory.FirstOrDefault(m => m.MatchId == match.MatchId) == null)
                {
                    // Set Players Match IDs since it's not in json
                    foreach (MatchHistoryPlayer player in match.Players)
                    {
                        player.MatchId = match.MatchId;
                    }
                    _dbContext.MatchHistory.Add(match);
                }
            }
            await _dbContext.SaveChangesAsync();

            // Call the match detail manually for each job of this since we might have new matches to fetch
            GetMatchDetailDataCallback(null);
        }
        catch (Exception ex)
        {
            // Handle exceptions here
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }
    private async void GetMatchDetailDataCallback(object? state)
    {
        try
        {
            // Find all the match histories without match detail rows and add tasks to fetch them all
            List<MatchHistory> matchesWithoutDetails = _dbContext.MatchHistory
                .GroupJoin(
                    _dbContext.MatchDetails,
                    match => match.MatchId,
                    details => details.MatchId,
                    (match, details) => new { Match = match, Details = details })
                .SelectMany(
                    m => m.Details.DefaultIfEmpty(),
                    (match, details) => new { match.Match, Details = details}
                )
                .Where(joinResult => joinResult.Details == null)
                .Select(joinResult => joinResult.Match)
                .ToList();

            if (matchesWithoutDetails.Count() > 0)
            {
                _logger.LogInformation($"Fetching {matchesWithoutDetails.Count()} new match details.");
                List<Task<MatchDetail?>> fetchMatchDetailsTasks = new List<Task<MatchDetail?>>();
                stopwatch.Start();

                foreach (MatchHistory match in matchesWithoutDetails)
                {
                    fetchMatchDetailsTasks.Add(GetMatchDetailsAsync(match.MatchId));
                }

                await Task.WhenAll(fetchMatchDetailsTasks);

                foreach (MatchDetail? matchDetail in fetchMatchDetailsTasks.Select(t => t.Result))
                {
                    if (matchDetail != null)
                    {
                        if (_dbContext.MatchDetails.FirstOrDefault(m => m.MatchId == matchDetail.MatchId) == null)
                        {
                            // Set PicksBans Match IDs since it's not in json
                            foreach (MatchDetailsPicksBans picksBans in matchDetail.PicksBans)
                            {
                                picksBans.MatchId = matchDetail.MatchId;
                            }

                            // Set Players Match IDs since it's not in json
                            foreach (MatchDetailsPlayer player in matchDetail.Players)
                            {
                                player.MatchId = matchDetail.MatchId;
                                // Set Players Ability Upgrade PlayerIDs since it's not in json
                                foreach (MatchDetailsPlayersAbilityUpgrade abilities in player.AbilityUpgrades)
                                {
                                    abilities.PlayerId = player.Id;
                                }
                            }

                            _dbContext.MatchDetails.Add(matchDetail);
                        }
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

    private async Task<List<MatchHistory>> GetMatchHistoryAsync(int leagueId)
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

            foreach (MatchHistory match in matchResponse)
            {
                match.LeagueId = leagueId;
            }

            matches.AddRange(matchResponse.Where(m => m.MatchId != startMatchId));

            if (resultsRemaining > 0)
            {
                // Response returns latest matches first
                startMatchId = matchResponse.OrderByDescending(m => m.MatchId).Last().MatchId;
            }
            else
            {
                endOfLeague = true;
            }
        }

        return matches;
    }

    private async Task<MatchDetail?> GetMatchDetailsAsync(long matchId)
    {
        await semaphoreSlim.WaitAsync();

        try
        {
            // Check if we're going too fast and need to wait
            TimeSpan elapsed = stopwatch.Elapsed;
            if (elapsed < delayBetweenRequests)
            {
                await Task.Delay(delayBetweenRequests - elapsed);
            }

            HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Get, _baseApiUrl);
            UriBuilder uriBuilder = new UriBuilder($"{_baseApiUrl}/GetMatchDetails/v1");
            uriBuilder.Query = $"key={_steamKey}&match_id={matchId}";

            httpRequest.RequestUri = uriBuilder.Uri;

            HttpResponseMessage response = await _httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            JToken responseRawJToken = JToken.Parse(await response.Content.ReadAsStringAsync());

            // Read and deserialize the matches from the json response
            JToken responseObject = responseRawJToken["result"] ?? "{}";

            MatchDetail? matchResponse = JsonConvert.DeserializeObject<MatchDetail>(responseObject.ToString());

            if(matchResponse != null)
            {
                matchResponse.MatchId = matchId;
            }

            return matchResponse;
        }
        finally
        {
            semaphoreSlim.Release();
        }
    }
}