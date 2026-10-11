using System.Drawing.Drawing2D;

namespace TraceBoard;

public sealed class BoardCanvas : Control
{
    public BoardDocument Document { get; private set; } = new();
    public string? SelectedCardId { get; private set; }
    public string? SelectedLinkId { get; private set; }
    public BoardCard? SelectedCard => Document.Cards.Find(c => c.Id == SelectedCardId);
    public BoardLink? SelectedLink => Document.Links.Find(c => c.Id == SelectedLinkId);
    public event Action? SelectionChanged;
    public event Action<string>? Moved;
    public event Action? EditRequested;
    public event Action? DeleteRequested;
    public event Action<PointF>? AddRequested;
    public event Action<string,string>? LinkRequested;
    public event Action<string,PointF>? ImageDropped;
    public event Action? ViewChanged;
    private readonly Dictionary<string,Bitmap> images = [];
    private PointF pan = new(30,30);
    private float zoom = 1;
    private string query = "";
    private bool linkMode;
    private string? linkSource;
    private bool panning;
    private bool space;
    private bool dragging;
    private Point startPoint;
    private Point lastPoint;
    private PointF original;
    private string? beforeDrag;
    private PointF pointer;
    public float Zoom => zoom;
    private float DpiScale => DeviceDpi / 96f;
    private float Factor => zoom * DpiScale;
    public bool LinkMode
    {
        get => linkMode;
        set { linkMode = value; linkSource = null; Cursor = value ? Cursors.Cross : Cursors.Default; Invalidate(); ViewChanged?.Invoke(); }
    }
    public string Query { get => query; set { query = value.Trim(); Invalidate(); ViewChanged?.Invoke(); } }
    public int MatchCount => Document.Cards.Count(Matches);

    public BoardCanvas()
    {
        DoubleBuffered = true; BackColor = Theme.Background; TabStop = true; AllowDrop = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        AccessibleName = "Доска расследования. Колесо — масштаб. Пробел и мышь — перемещение. Двойной щелчок — редактор.";
    }
    public void SetDocument(BoardDocument doc, bool fit = false)
    {
        FinishDrag(); Document = doc; SelectedCardId = SelectedLinkId = linkSource = null;
        linkMode=false; Cursor=Cursors.Default;
        ClearImages(); if (fit) Fit(); Invalidate(); SelectionChanged?.Invoke(); ViewChanged?.Invoke();
    }
    public void RefreshContent(bool clearImages = false)
    {
        if (clearImages) ClearImages(); Invalidate(); SelectionChanged?.Invoke(); ViewChanged?.Invoke();
    }
    public void SelectCard(string? id)
    {
        SelectedCardId = id; SelectedLinkId = null; Invalidate(); SelectionChanged?.Invoke();
    }
    public PointF CenterWorld => ToWorld(new Point(Width / 2, Height / 2));
    public PointF ToWorld(Point p) => new((p.X - pan.X) / Factor, (p.Y - pan.Y) / Factor);
    private bool Matches(BoardCard c) => query.Length == 0 || (c.Title + " " + c.Body).Contains(query, StringComparison.OrdinalIgnoreCase);
    public void LocateMatch()
    {
        var match = Document.Cards.FirstOrDefault(Matches);
        if (match is null) return; SelectCard(match.Id);
        pan = new(Width / 2f - match.Center.X * Factor, Height / 2f - match.Center.Y * Factor); Invalidate();
    }
    public void SetZoom(float next)
    {
        ZoomAt(next, new Point(Width / 2, Height / 2));
    }
    private void ZoomAt(float next, Point focus)
    {
        var world = ToWorld(focus); zoom = Math.Clamp(next, .25f, 2f);
        pan = new(focus.X - world.X * Factor, focus.Y - world.Y * Factor);
        Invalidate(); ViewChanged?.Invoke();
    }
    public void Fit()
    {
        var bounds = ContentBounds();
        zoom = Math.Clamp(Math.Min((Width - 90 * DpiScale) / (bounds.Width * DpiScale), (Height - 90 * DpiScale) / (bounds.Height * DpiScale)), .25f, 1.2f);
        pan = new((Width - bounds.Width * Factor) / 2 - bounds.X * Factor, (Height - bounds.Height * Factor) / 2 - bounds.Y * Factor);
        Invalidate(); ViewChanged?.Invoke();
    }
    private RectangleF ContentBounds()
    {
        if (Document.Cards.Count == 0) return new(0,0,900,560);
        var bounds = Document.Cards[0].Bounds;
        foreach (var c in Document.Cards.Skip(1)) bounds = RectangleF.Union(bounds,c.Bounds);
        return bounds;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g=e.Graphics; g.Clear(Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        float step = 30 * Factor;
        if (step >= 8)
        {
            using var dots = new SolidBrush(Color.FromArgb(42,42,42));
            for (float x = pan.X % step; x < Width; x += step)
                for (float y = pan.Y % step; y < Height; y += step) g.FillEllipse(dots,x,y,1.4f * DpiScale,1.4f * DpiScale);
        }
        var state=g.Save(); g.TranslateTransform(pan.X,pan.Y); g.ScaleTransform(Factor,Factor);
        DrawBoard(g,true);
        if (linkMode && linkSource is not null && Document.Cards.Find(c=>c.Id==linkSource) is { } source)
        {
            using var p = new Pen(Color.White,1.5f / zoom) { DashStyle = DashStyle.Dash };
            g.DrawLine(p,source.Center,pointer);
        }
        g.Restore(state);
        if (Document.Cards.Count == 0)
        {
            g.ScaleTransform(DpiScale,DpiScale);
            Theme.DrawText(g,"Каждая история начинается с детали.",new(48,58,620,46),24,Theme.Text,true);
            Theme.DrawText(g,"Добавьте карточку или перетащите сюда фотографию.\nДвойной щелчок по пустой доске создаёт заметку.",new(50,118,570,65),14,Theme.Muted);
        }
        if (linkMode)
        {
            g.ResetTransform(); var rect = new RectangleF(18 * DpiScale,18 * DpiScale,360 * DpiScale,38 * DpiScale);
            Theme.RoundFill(g,rect,Theme.Text);
            Theme.DrawText(g,linkSource is null ? "Выберите первую карточку · Esc — отмена" : "Теперь выберите вторую карточку",new(rect.X+12,rect.Y+10,rect.Width-20,rect.Height-10),12 * DpiScale,Theme.Background,true);
        }
    }
    private void DrawBoard(Graphics g, bool interactive)
    {
        foreach (var link in Document.Links)
        {
            var a=Document.Cards.Find(c=>c.Id==link.From); var b=Document.Cards.Find(c=>c.Id==link.To);
            if (a is null || b is null) continue;
            bool selected=interactive && (SelectedLinkId==link.Id || SelectedCardId==a.Id || SelectedCardId==b.Id);
            Color color=selected ? Theme.Text : Color.FromArgb(94,94,94);
            using var pen=new Pen(color,selected ? 2 : 1.3f);
            var start=Edge(a.Bounds,b.Center); var end=Edge(b.Bounds,a.Center);
            g.DrawLine(pen,start,end);
            using var brush=new SolidBrush(color); g.FillEllipse(brush,start.X-3,start.Y-3,6,6); g.FillEllipse(brush,end.X-3,end.Y-3,6,6);
            var label=LinkLabelBounds(g,link,start,end);
            Theme.RoundFill(g,label,Theme.Background,4);
            Theme.DrawText(g,link.Label,new(label.X+8,label.Y+5,label.Width-16,18),10,selected ? Theme.Text : Theme.Muted);
        }
        foreach (var card in Document.Cards)
        {
            bool selected=interactive && SelectedCardId==card.Id;
            var r=card.Bounds;
            Theme.RoundFill(g,new(r.X+3,r.Y+5,r.Width,r.Height),Color.FromArgb(8,8,8));
            Theme.RoundFill(g,r,Theme.Card);
            using (var outline=Theme.Rounded(r,7))
            using (var pen=new Pen(selected ? Color.White : Theme.Border,selected ? 2 : 1)) g.DrawPath(pen,outline);
            Theme.DrawText(g,Theme.Kind(card.Kind),new(r.X+16,r.Y+15,r.Width-54,18),10,Theme.Muted,true);
            using (var dot=new SolidBrush(selected ? Color.White : Color.FromArgb(86,86,86))) g.FillEllipse(dot,r.Right-24,r.Y+18,6,6);
            Theme.DrawText(g,card.Title,new(r.X+15,r.Y+43,r.Width-30,47),17,Theme.Text,true);
            float bodyY=r.Y+96;
            if (card.ImageData is not null)
            {
                var image=GetImage(card);
                var imageRect=new RectangleF(r.X+15,r.Y+96,r.Width-30,113);
                if (image is not null)
                {
                    float ratio=Math.Min(imageRect.Width/image.Width,imageRect.Height/image.Height);
                    float w=image.Width*ratio,h=image.Height*ratio;
                    g.DrawImage(image,new RectangleF(imageRect.X+(imageRect.Width-w)/2,imageRect.Y+(imageRect.Height-h)/2,w,h));
                }
                bodyY=r.Y+223;
            }
            Theme.DrawText(g,card.Body.Replace('\r',' '),new(r.X+16,bodyY,r.Width-32,card.ImageData is null ? 53 : 27),11,Theme.Muted);
            using(var line=new Pen(Theme.Border))g.DrawLine(line,r.X+16,r.Bottom-27,r.Right-16,r.Bottom-27);
            Theme.DrawText(g,$"{Document.Links.Count(l=>l.From==card.Id || l.To==card.Id)} связей",new(r.X+16,r.Bottom-20,r.Width-32,15),9,Theme.Muted);
            if (interactive && !Matches(card)) Theme.RoundFill(g,r,Color.FromArgb(175,14,14,14));
        }
    }
    private Bitmap? GetImage(BoardCard card)
    {
        if (images.TryGetValue(card.Id,out var image)) return image;
        if (card.ImageData is null) return null;
        try { using var stream=new MemoryStream(Convert.FromBase64String(card.ImageData)); using var original=Image.FromStream(stream); image=new Bitmap(original); images[card.Id]=image; return image; }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OutOfMemoryException) { return null; }
    }
    private static PointF Edge(RectangleF r, PointF target)
    {
        float cx=r.X+r.Width/2,cy=r.Y+r.Height/2,dx=target.X-cx,dy=target.Y-cy;
        if(Math.Abs(dx)+Math.Abs(dy)<.01) return new(cx,cy);
        float t=Math.Min(dx==0 ? float.MaxValue : r.Width/2/Math.Abs(dx),dy==0 ? float.MaxValue : r.Height/2/Math.Abs(dy));
        return new(cx+dx*t,cy+dy*t);
    }
    private static RectangleF LinkLabelBounds(Graphics g,BoardLink link,PointF start,PointF end)
    {
        float width=Math.Min(180,g.MeasureString(link.Label,Theme.Font(10)).Width+16);
        return new((start.X+end.X-width)/2,(start.Y+end.Y)/2-13,width,26);
    }
    private BoardCard? HitCard(PointF p) => Document.Cards.AsEnumerable().Reverse().FirstOrDefault(c=>c.Bounds.Contains(p));
    private BoardLink? HitLink(PointF p)
    {
        foreach(var link in Document.Links.AsEnumerable().Reverse())
        {
            var a=Document.Cards.Find(c=>c.Id==link.From); var b=Document.Cards.Find(c=>c.Id==link.To);
            if(a is null || b is null)continue;
            var start=Edge(a.Bounds,b.Center);var end=Edge(b.Bounds,a.Center);
            float dx=end.X-start.X,dy=end.Y-start.Y,den=dx*dx+dy*dy;
            float t=den==0 ? 0 : Math.Clamp(((p.X-start.X)*dx+(p.Y-start.Y)*dy)/den,0,1);
            float ex=p.X-(start.X+t*dx),ey=p.Y-(start.Y+t*dy);
            if(ex*ex+ey*ey < 100/(zoom*zoom))return link;
        }
        return null;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus(); pointer=ToWorld(e.Location); startPoint=lastPoint=e.Location;
        if(e.Button is MouseButtons.Middle or MouseButtons.Right || space)
        { panning=true; Capture=true; Cursor=Cursors.SizeAll; return; }
        if(e.Button!=MouseButtons.Left)return;
        var card=HitCard(pointer);
        if(linkMode)
        {
            if(card is null)return;
            SelectCard(card.Id);
            if(linkSource is null)linkSource=card.Id;
            else if(linkSource!=card.Id) { string source=linkSource; LinkMode=false; LinkRequested?.Invoke(source,card.Id); }
            Invalidate();return;
        }
        if(card is not null)
        {
            SelectCard(card.Id); dragging=true; Capture=true; original=new(card.X,card.Y); beforeDrag=BoardFiles.Serialize(Document); Cursor=Cursors.SizeAll;
        }
        else
        {
            SelectedCardId=null;SelectedLinkId=HitLink(pointer)?.Id;SelectionChanged?.Invoke();
            if(SelectedLinkId is null){panning=true;Capture=true;Cursor=Cursors.SizeAll;} Invalidate();
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);pointer=ToWorld(e.Location);
        if(panning){pan.X+=e.X-lastPoint.X;pan.Y+=e.Y-lastPoint.Y;}
        else if(dragging && SelectedCard is { } card)
        {
            card.X=Math.Clamp(original.X+(e.X-startPoint.X)/Factor,-90000,90000);
            card.Y=Math.Clamp(original.Y+(e.Y-startPoint.Y)/Factor,-90000,90000);
        }
        lastPoint=e.Location;
        if(panning || dragging || linkMode)Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);FinishDrag();panning=false;Capture=false;Cursor=linkMode?Cursors.Cross:Cursors.Default;
        if(e.Button==MouseButtons.Right && Math.Abs(e.X-startPoint.X)+Math.Abs(e.Y-startPoint.Y)<5)
        {
            var point=ToWorld(e.Location);var hit=HitCard(point);if(hit is not null)SelectCard(hit.Id);
            var menu=new ContextMenuStrip { BackColor=Theme.Card,ForeColor=Theme.Text,Font=Theme.Font() };
            if(hit is not null)
            {
                menu.Items.Add("Редактировать",null,(_,_)=>EditRequested?.Invoke());
                menu.Items.Add("Создать связь",null,(_,_)=>{LinkMode=true;linkSource=hit.Id;Invalidate();});
                menu.Items.Add("Удалить карточку",null,(_,_)=>DeleteRequested?.Invoke());
            }
            else menu.Items.Add("Новая заметка здесь",null,(_,_)=>AddRequested?.Invoke(point));
            menu.Closed+=(_,_)=>menu.Dispose();menu.Show(this,e.Location);
        }
    }
    private void FinishDrag()
    {
        if(dragging && SelectedCard is { } c && beforeDrag is not null && (c.X!=original.X || c.Y!=original.Y))Moved?.Invoke(beforeDrag);
        dragging=false;beforeDrag=null;
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {base.OnMouseCaptureChanged(e);if(!Capture){FinishDrag();panning=false;}}
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);if(e.Button!=MouseButtons.Left || linkMode)return;FinishDrag();
        if(SelectedCard is not null || SelectedLink is not null)EditRequested?.Invoke(); else AddRequested?.Invoke(ToWorld(e.Location));
    }
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(!dragging)ZoomAt(zoom*(e.Delta>0?1.12f:1/1.12f),e.Location);}
    protected override bool IsInputKey(Keys keyData)=>keyData==Keys.Space || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Space){space=true;e.Handled=true;}if(e.KeyCode==Keys.Escape){LinkMode=false;e.Handled=true;}}
    protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);if(e.KeyCode==Keys.Space)space=false;}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);space=false;}
    protected override void OnDragEnter(DragEventArgs e){base.OnDragEnter(e);e.Effect=e.Data?.GetDataPresent(DataFormats.FileDrop)==true ? DragDropEffects.Copy : DragDropEffects.None;}
    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);if(e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length>0)ImageDropped?.Invoke(files[0],ToWorld(PointToClient(new(e.X,e.Y))));
    }
    public Bitmap Export()
    {
        var bounds=ContentBounds();bounds.Inflate(50,50);
        float factor=Math.Min(1.5f,Math.Min(5000/bounds.Width,5000/bounds.Height));
        var bitmap=new Bitmap(Math.Max(1,(int)(bounds.Width*factor)),Math.Max(1,(int)(bounds.Height*factor)));
        using var g=Graphics.FromImage(bitmap);g.Clear(Theme.Background);g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.ScaleTransform(factor,factor);g.TranslateTransform(-bounds.X,-bounds.Y);DrawBoard(g,false);return bitmap;
    }
    private void ClearImages(){foreach(var image in images.Values)image.Dispose();images.Clear();}
    protected override void Dispose(bool disposing){if(disposing)ClearImages();base.Dispose(disposing);}
}
