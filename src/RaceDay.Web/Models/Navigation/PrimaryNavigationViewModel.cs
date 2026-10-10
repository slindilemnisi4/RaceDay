using System.Security.Claims;

namespace RaceDay.Web.Models.Navigation;

public sealed class PrimaryNavigationViewModel
{
    private PrimaryNavigationViewModel(
        string label,
        IReadOnlyList<NavigationItem> items,
        string? currentController,
        string? currentAction)
    {
        Label = label;
        Items = items;
        CurrentController = currentController;
        CurrentAction = currentAction;
    }

    public string Label { get; }

    public IReadOnlyList<NavigationItem> Items { get; }

    public string? CurrentController { get; }

    public string? CurrentAction { get; }

    public static PrimaryNavigationViewModel Create(
        ClaimsPrincipal user,
        string? currentController,
        string? currentAction)
    {
        if (user.IsInRole("Participant"))
        {
            return new PrimaryNavigationViewModel(
                "Participant navigation",
                [
                    new("Home", "home", "Home", "Index"),
                    new("My Races", "races", null, null),
                    new("Join Race", "join", null, null),
                    new("My Results", "results", null, null),
                    new("Profile", "profile", null, null)
                ],
                currentController,
                currentAction);
        }

        if (user.IsInRole("Organiser"))
        {
            return new PrimaryNavigationViewModel(
                "Organiser navigation",
                [
                    new("Home", "home", "Home", "Index"),
                    new("My Events", "events", null, null),
                    new("Add Race", "add", null, null),
                    new("Enrolments & Results", "results", null, null),
                    new("Profile", "profile", null, null)
                ],
                currentController,
                currentAction);
        }

        return new PrimaryNavigationViewModel(
            "Main navigation",
            [],
            currentController,
            currentAction);
    }
}

public sealed record NavigationItem(
    string Label,
    string Icon,
    string? Controller,
    string? Action)
{
    public bool IsImplemented => Controller is not null && Action is not null;
}
