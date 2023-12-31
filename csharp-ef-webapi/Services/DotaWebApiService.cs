using System.Diagnostics;
using csharp_ef_webapi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace csharp_ef_webapi.Services;
public class DotaWebApiService
{
    private readonly string _baseApiUrl;
    private readonly string _econApiUrl;
    private readonly string _steamKey;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DotaWebApiService> _logger;
    private readonly AghanimsWagerContext _dbContext;

    // Serialized tasks nothing concurrent, because we dependency inject the dbContext if we run parallel tasks it's
    // going to blow up if it tries to commit two transactions at the same time with the same context.
    private static readonly SemaphoreSlim dbContextSemaphoreSlim = new SemaphoreSlim(1);
    private static long taskDelayTicker = 0;
    private static readonly TimeSpan delayBetweenRequests = TimeSpan.FromSeconds(1.0 / 2); // 2 requests per second
    IConfiguration _configuration;

    public DotaWebApiService(ILogger<DotaWebApiService> logger, IConfiguration configuration, AghanimsWagerContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
        _configuration = configuration;

        _baseApiUrl = _configuration.GetSection("DotaWebApi").GetValue<string>("BaseUrl");
        _econApiUrl = _configuration.GetSection("DotaWebApi").GetValue<string>("EconUrl");
        _steamKey = Environment.GetEnvironmentVariable("STEAM_KEY") ?? "";

        _httpClient = new HttpClient();
        _logger.LogInformation("Dota WebApi Service started");
        // We're running this once a day because the live games should get us the updates
        new Timer(GetLeagueHistoryDataCallback, null, TimeSpan.Zero, TimeSpan.FromMinutes(30));

        // We're running this more frequently because it should typically find no new match details and skip
        new Timer(GetMatchDetailDataCallback, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));

        // Heroes update like once a year
        new Timer(GetHeroesDataCallback, null, TimeSpan.Zero, TimeSpan.FromDays(1));
    }

    private async void GetLeagueHistoryDataCallback(object? state)
    {
        // Get all the Match Histories for the leagues, this is intended to be run first
        try
        {

            List<League> leagues = _dbContext.Leagues.Where(l => l.isActive).ToList();
            _logger.LogInformation($"Fetching league matches for {leagues.Count()} leagues");

            List<Task<List<MatchHistory>>> fetchMatchHistoryTasks = new List<Task<List<MatchHistory>>>();

            foreach (League league in leagues)
            {
                fetchMatchHistoryTasks.Add(GetMatchHistoryAsync(league.id));
            }

            await Task.WhenAll(fetchMatchHistoryTasks);

            // Lock the context so other tasks don't try to perform transactions
            await dbContextSemaphoreSlim.WaitAsync();

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

            // Done with dbContext so release it for anything else
            dbContextSemaphoreSlim.Release();

            _logger.LogInformation($"League Match History fetch done");

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
                    (match, details) => new { match.Match, Details = details }
                )
                .Where(joinResult => joinResult.Details == null)
                .Select(joinResult => joinResult.Match)
                .ToList();

            if (matchesWithoutDetails.Count() > 0)
            {
                _logger.LogInformation($"Fetching {matchesWithoutDetails.Count()} new match details.");
                List<Task<MatchDetail?>> fetchMatchDetailsTasks = new List<Task<MatchDetail?>>();

                foreach (MatchHistory match in matchesWithoutDetails)
                {
                    fetchMatchDetailsTasks.Add(GetMatchDetailsAsync(match.MatchId));
                }

                await Task.WhenAll(fetchMatchDetailsTasks);

                await dbContextSemaphoreSlim.WaitAsync();

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

                dbContextSemaphoreSlim.Release();

                _logger.LogInformation($"Missing match details fetch done");

            }

        }
        catch (Exception ex)
        {
            // Handle exceptions here
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }

    private async void GetHeroesDataCallback(object? state)
    {
        await dbContextSemaphoreSlim.WaitAsync();

        try
        {
            _logger.LogInformation($"Fetching heroes");

            List<Hero> heroes = new List<Hero>();
            heroes = await GetHeroesAsync();

            await dbContextSemaphoreSlim.WaitAsync();

            foreach (Hero hero in heroes)
            {
                if (_dbContext.Heroes.FirstOrDefault(h => h.Id == hero.Id) == null)
                {
                    _dbContext.Heroes.Add(hero);
                }
                else
                {
                    Hero updateHero = _dbContext.Heroes.First(h => h.Id == hero.Id);
                    updateHero.Name = hero.Name;
                    _dbContext.Heroes.Update(updateHero);
                }
            }
            await _dbContext.SaveChangesAsync();

            dbContextSemaphoreSlim.Release();

            _logger.LogInformation($"Hero fetch done");
        }
        catch (Exception ex)
        {
            // Handle exceptions here
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
        finally
        {
            dbContextSemaphoreSlim.Release();
        }
    }

    private async Task<List<MatchHistory>> GetMatchHistoryAsync(int leagueId)
    {
        List<MatchHistory> matches = new List<MatchHistory>();
        bool endOfLeague = false;
        long? startMatchId = null;
        while (!endOfLeague)
        {
            await WaitNextTaskScheduleAsync();

            HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Get, _baseApiUrl);
            UriBuilder uriBuilder = new UriBuilder($"{_baseApiUrl}/GetMatchHistory/v1");
            uriBuilder.Query = $"key={_steamKey}&league_id={leagueId}&matches_requested=100";

            if (startMatchId.HasValue)
            {
                uriBuilder.Query += $"&start_at_match_id={startMatchId}";
            }

            httpRequest.RequestUri = uriBuilder.Uri;

            HttpResponseMessage response = await _httpClient.SendAsync(httpRequest);
            _logger.LogInformation($"Request submitted at {DateTime.Now.Ticks}");
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
        await WaitNextTaskScheduleAsync();

        HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Get, _baseApiUrl);
        UriBuilder uriBuilder = new UriBuilder($"{_baseApiUrl}/GetMatchDetails/v1");
        uriBuilder.Query = $"key={_steamKey}&match_id={matchId}";

        httpRequest.RequestUri = uriBuilder.Uri;

        HttpResponseMessage response = await _httpClient.SendAsync(httpRequest);
        _logger.LogInformation($"Request submitted at {DateTime.Now.Ticks}");
        response.EnsureSuccessStatusCode();

        JToken responseRawJToken = JToken.Parse(await response.Content.ReadAsStringAsync());

        // Read and deserialize the matches from the json response
        JToken responseObject = responseRawJToken["result"] ?? "{}";

        MatchDetail? matchResponse = JsonConvert.DeserializeObject<MatchDetail>(responseObject.ToString());

        if (matchResponse != null)
        {
            matchResponse.MatchId = matchId;
        }

        return matchResponse;
    }

    private async Task<List<Hero>> GetHeroesAsync()
    {
        await WaitNextTaskScheduleAsync();

        HttpRequestMessage httpRequest = new HttpRequestMessage(HttpMethod.Get, _baseApiUrl);
        UriBuilder uriBuilder = new UriBuilder($"{_econApiUrl}/GetHeroes/v1");
        uriBuilder.Query = $"key={_steamKey}";

        httpRequest.RequestUri = uriBuilder.Uri;

        HttpResponseMessage response = await _httpClient.SendAsync(httpRequest);
        _logger.LogInformation($"Request submitted at {DateTime.Now.Ticks}");
        response.EnsureSuccessStatusCode();

        JToken responseRawJToken = JToken.Parse(await response.Content.ReadAsStringAsync());

        // Read and deserialize the matches from the json response
        JToken responseObject = responseRawJToken["result"] ?? "{}";

        List<Hero> heroesResponse = JsonConvert.DeserializeObject<List<Hero>>(responseObject.ToString()) ?? new List<Hero>();

        return heroesResponse;
    }

    private async Task WaitNextTaskScheduleAsync()
    {
        bool isScheduled = false;
        long lastTaskScheduled;
        long currentTime;
        while (!isScheduled)
        {
            // Get the time at this loop
            currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            lastTaskScheduled = taskDelayTicker;

            // Check if the time has passed the last task + delay between
            if (currentTime - lastTaskScheduled >= delayBetweenRequests.Milliseconds)
            {
                // If it has passed the window try to exchange with the new schedule
                if (Interlocked.CompareExchange(ref taskDelayTicker, currentTime, lastTaskScheduled) == lastTaskScheduled)
                {
                    isScheduled = true;
                }

            }

            // Either current time is too early, or the compare exchange didn't go in time, so delay the difference and try again at the next delay cycle
            int delay = Math.Max(0, (int)(500 - (currentTime - taskDelayTicker)));
            await Task.Delay(delay);
        }
        return;
    }
}