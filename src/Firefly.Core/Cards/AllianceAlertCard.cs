namespace Firefly.Core.Cards
{
    public enum AllianceAlertParkKind
    {
        None,
        Contact,
        Supply
    }

    /// <summary>
    /// Where an Active Alert currently sits when Alliance Audit or
    /// Criminal Sighting has moved it off the open table and onto a deck.
    /// </summary>
    public sealed class AllianceAlertPark
    {
        public AllianceAlertParkKind Kind { get; }
        public string Target { get; }

        public AllianceAlertPark(AllianceAlertParkKind kind, string target)
        {
            Kind = kind;
            Target = target ?? string.Empty;
        }

        public static AllianceAlertPark Contact(string name) =>
            new AllianceAlertPark(AllianceAlertParkKind.Contact, name);

        public static AllianceAlertPark Supply(string planet) =>
            new AllianceAlertPark(AllianceAlertParkKind.Supply, planet);

        public override string ToString() =>
            Kind == AllianceAlertParkKind.None ? ""
            : Kind == AllianceAlertParkKind.Contact ? "Contact:" + Target
            : "Supply:" + Target;
    }

    public sealed class AllianceAlertCard
    {
        public string Id { get; }
        public string Name { get; }
        public string? Source { get; }
        public string? SourceLabel { get; }
        public string? Detail { get; }
        public bool SystemWide { get; }
        public string? SystemWideText { get; }

        public AllianceAlertCard(
            string id,
            string name,
            string? source,
            string? sourceLabel,
            string? detail,
            bool systemWide,
            string? systemWideText)
        {
            Id = id;
            Name = name;
            Source = source;
            SourceLabel = sourceLabel;
            Detail = detail;
            SystemWide = systemWide;
            SystemWideText = systemWideText;
        }
    }
}
