using System.ComponentModel.DataAnnotations;

namespace RaceDay.Web.Models.Events;

public sealed class JoinEventViewModel
{
    [Range(1, int.MaxValue)]
    public int EventID { get; init; }

    [Range(1, int.MaxValue)]
    public int CategoryID { get; init; }
}
