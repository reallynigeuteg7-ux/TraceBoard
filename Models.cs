using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraceBoard;

public enum CardKind { Person, Evidence, Place, Note }

public sealed class BoardCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CardKind Kind { get; set; } = CardKind.Note;
    public string Title { get; set; } = "Новая карточка";
    public string Body { get; set; } = "";
    public string? ImageData { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    [JsonIgnore] public float Height => ImageData is null ? 184 : 280;
    [JsonIgnore] public RectangleF Bounds => new(X, Y, 244, Height);
    [JsonIgnore] public PointF Center => new(X + 122, Y + Height / 2);
}

public sealed class BoardLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Label { get; set; } = "связь";
}

public sealed class BoardDocument
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "Новое расследование";
    public List<BoardCard> Cards { get; set; } = [];
    public List<BoardLink> Links { get; set; } = [];

    public static BoardDocument Demo()
    {
        var doc = new BoardDocument { Title = "Дело 001 · Последний сеанс" };
        doc.Cards =
        [
            new() { Id = "a", Kind = CardKind.Person, X = 50, Y = 55, Title = "Алексей Ветров", Body = "Киномеханик\nПоследним покинул зал в 21:40. Говорит, что оставил ключ на стойке." },
            new() { Id = "b", Kind = CardKind.Evidence, X = 405, Y = 55, Title = "Билет № 017", Body = "Найден рядом с проектором.\nНа обороте — время 22:15 и слово «север»." },
            new() { Id = "c", Kind = CardKind.Place, X = 760, Y = 55, Title = "Кинотеатр «Орбита»", Body = "Северный вход\nЗапасная дверь ведёт к архиву. Камера не работает с понедельника." },
            new() { Id = "d", Kind = CardKind.Evidence, X = 50, Y = 370, Title = "Пропавшая плёнка", Body = "Катушка из архивной коллекции.\nВ журнале выдачи нет записи за этот день." },
            new() { Id = "e", Kind = CardKind.Note, X = 405, Y = 370, Title = "Проверить алиби", Body = "Кто был в здании после сеанса?\nСопоставить время на билете с журналом охраны." },
            new() { Id = "f", Kind = CardKind.Person, X = 760, Y = 370, Title = "Ирина Соколова", Body = "Архивист\nНазначила встречу у северного входа. Утверждает, что её отменили." }
        ];
        doc.Links =
        [
            new() { From = "a", To = "b", Label = "нашёл" },
            new() { From = "b", To = "c", Label = "указывает на" },
            new() { From = "a", To = "d", Label = "имел доступ" },
            new() { From = "b", To = "e", Label = "время не сходится" },
            new() { From = "c", To = "f", Label = "место встречи" },
            new() { From = "e", To = "f", Label = "уточнить" }
        ];
        return doc;
    }
}

public static class BoardFiles
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static string Serialize(BoardDocument doc) => JsonSerializer.Serialize(doc, Options);
    public static BoardDocument Parse(string json)
    {
        var doc = JsonSerializer.Deserialize<BoardDocument>(json) ?? throw new InvalidDataException("Пустой файл доски.");
        if (doc.Version != 1) throw new InvalidDataException("Эта версия файла не поддерживается.");
        if (doc.Cards is null || doc.Links is null || doc.Cards.Count > 500 || doc.Links.Count > 2000)
            throw new InvalidDataException("Доска повреждена или слишком велика (до 500 карточек и 2000 связей).");
        var ids = new HashSet<string>();
        foreach (var card in doc.Cards)
        {
            if (card is null || string.IsNullOrWhiteSpace(card.Id) || !ids.Add(card.Id) || !float.IsFinite(card.X) || !float.IsFinite(card.Y) || Math.Abs(card.X) > 100000 || Math.Abs(card.Y) > 100000)
                throw new InvalidDataException("Неверные данные карточки.");
            card.Title ??= "Без названия";
            card.Title = card.Title[..Math.Min(card.Title.Length, 120)];
            card.Body ??= "";
            if (card.Body.Length > 20000 || (card.ImageData?.Length ?? 0) > 12_000_000 || !Enum.IsDefined(card.Kind))
                throw new InvalidDataException("Слишком большой текст или изображение в карточке.");
            if (card.ImageData is not null)
            {
                try
                {
                    using var stream = new MemoryStream(Convert.FromBase64String(card.ImageData));
                    using var image = Image.FromStream(stream, false, true);
                    if (image.Width > 12000 || image.Height > 12000) throw new InvalidDataException("Изображение слишком велико.");
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException or OutOfMemoryException)
                { throw new InvalidDataException("Повреждено изображение карточки.", ex); }
            }
        }
        var linkIds = new HashSet<string>();
        if (doc.Links.Any(link => link is null || string.IsNullOrWhiteSpace(link.Id) || !linkIds.Add(link.Id) || !ids.Contains(link.From) || !ids.Contains(link.To) || link.From == link.To))
            throw new InvalidDataException("Некорректные связи на доске.");
        foreach (var link in doc.Links) link.Label = (link.Label ?? "связь")[..Math.Min(link.Label?.Length ?? 5, 100)];
        doc.Title = string.IsNullOrWhiteSpace(doc.Title) ? "Без названия" : doc.Title[..Math.Min(doc.Title.Length, 120)];
        return doc;
    }
    public static BoardDocument Read(string path)
    {
        if (new FileInfo(path).Length > 100_000_000) throw new InvalidDataException("Файл больше 100 МБ.");
        return Parse(File.ReadAllText(path));
    }
    public static void Write(string path, BoardDocument doc)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, Serialize(doc));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class BoardHistory
{
    private readonly List<string> undo = [];
    private readonly List<string> redo = [];
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Clear() { undo.Clear(); redo.Clear(); }
    public void Record(string before)
    {
        undo.Add(before); redo.Clear();
        while (undo.Count > 30 || undo.Count > 1 && undo.Sum(s => (long)s.Length) > 32_000_000) undo.RemoveAt(0);
    }
    public BoardDocument? Undo(BoardDocument current) => Travel(undo, redo, current);
    public BoardDocument? Redo(BoardDocument current) => Travel(redo, undo, current);
    private static BoardDocument? Travel(List<string> from, List<string> to, BoardDocument current)
    {
        if (from.Count == 0) return null;
        var result = BoardFiles.Parse(from[^1]); from.RemoveAt(from.Count - 1);
        to.Add(BoardFiles.Serialize(current)); return result;
    }
}
