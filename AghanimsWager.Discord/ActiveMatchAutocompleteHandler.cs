using Discord;
using Discord.Interactions;

namespace AghanimsWager.Discord;

public class ActiveMatchAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var bot = (WagerBot)services.GetService(typeof(WagerBot))!;
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";

        var suggestions = bot.GetBettableLobbies()
            .Where(l => string.IsNullOrEmpty(typed) || l.MatchId.ToString().Contains(typed))
            .Take(25)
            .Select(l => new AutocompleteResult($"Match {l.MatchId} — {l.GamblingStatus}", l.MatchId));

        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }
}
