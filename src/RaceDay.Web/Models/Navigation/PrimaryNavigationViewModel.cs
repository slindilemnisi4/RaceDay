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
                    new("My Races", "races", "Participant", "MyRaces"),
                    new("Join Race", "join", "Participant", "Join"),
                    new("My Results", "results", "Participant", "MyResults"),
                    new("Profile", "profile", "Participant", "Profile")
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
                    new("My Events", "events", "Organiser", "MyEvents"),
                    new("Add Race", "add", "Organiser", "CreateEvent"),
                    new("Enrolments & Results", "results", "Organiser", "Enrolments"),
                    new("Profile", "profile", "Organiser", "Profile")
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
