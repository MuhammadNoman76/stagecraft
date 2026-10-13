using Stagecraft.Core.Import;

namespace Stagecraft.Core.IO;

/// <summary>Opens what Stagecraft can open: its own decks and PowerPoint files (imported).</summary>
public static class DocumentLoader
{
    public static bool IsPowerPoint(string path) =>
        string.Equals(Path.GetExtension(path), ".pptx", StringComparison.OrdinalIgnoreCase);

    public static bool IsDeck(string path) =>
        string.Equals(Path.GetExtension(path), DeckFile.Extension, StringComparison.OrdinalIgnoreCase);

    public static DeckDocument Load(string path) => IsPowerPoint(path) ? PptxImporter.Import(path) : DeckFile.Load(path);
}
