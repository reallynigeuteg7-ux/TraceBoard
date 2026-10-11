using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TraceBoard;

public sealed class MainForm : Form
{
    private readonly BoardCanvas canvas=new(){Dock=DockStyle.Fill};
    private readonly BoardHistory history=new();
    private readonly Label boardTitle=Theme.Label("",17,true);
    private readonly Label summary=Theme.Label("",11);
    private readonly Label status=Theme.Label("",11);
    private readonly Label zoomLabel=Theme.Label("100%",11,true);
    private readonly Label detailType=Theme.Label("НА ДОСКЕ",10,true);
    private readonly Label detailTitle=Theme.Label("",20,true);
    private readonly Label detailBody=Theme.Label("",12);
    private readonly Label detailHint=Theme.Label("",11);
    private readonly PictureBox detailImage=new(){SizeMode=PictureBoxSizeMode.Zoom,BackColor=Theme.Background};
    private readonly TextBox search=Theme.Input();
    private readonly Button undoButton;
    private readonly Button redoButton;
    private readonly Button connectButton;
    private readonly Button editButton;
    private readonly Button deleteButton;
    private readonly Button relateButton;
    private readonly System.Windows.Forms.Timer autosave=new(){Interval=900};
    private readonly string recovery=Path.Combine(AppContext.BaseDirectory,"Data","Recovery.traceboard");
    private string? filePath;
    private bool dirty;
    private bool saving;
    private string notice="";
    private BoardDocument Document=>canvas.Document;

    public MainForm()
    {
        Text="TraceBoard — доска расследований";ClientSize=new(1400,860);MinimumSize=new(1140,780);
        StartPosition=FormStartPosition.CenterScreen;BackColor=Theme.Background;ForeColor=Theme.Text;Font=Theme.Font();
        AutoScaleMode=AutoScaleMode.Dpi;KeyPreview=true;
        Icon=new Icon(Path.Combine(AppContext.BaseDirectory,"Assets","traceboard.ico"));
        undoButton=Theme.Button("←",Undo,false,34);undoButton.AccessibleName="Отменить действие";
        redoButton=Theme.Button("→",Redo,false,34);redoButton.AccessibleName="Повторить действие";
        connectButton=Theme.Button("Связь",()=>{canvas.LinkMode=!canvas.LinkMode;canvas.Focus();},false,82);
        editButton=Theme.Button("Редактировать",EditSelected,true,224);
        relateButton=Theme.Button("Связать с карточкой",()=>{canvas.LinkMode=true;canvas.Focus();},false,224);
        deleteButton=Theme.Button("Удалить",DeleteSelected,false,224);
        BuildLayout();
        canvas.SelectionChanged+=UpdateInspector;canvas.ViewChanged+=UpdateStatus;
        canvas.Moved+=before=>{history.Record(before);Changed();};
        canvas.EditRequested+=EditSelected;canvas.DeleteRequested+=DeleteSelected;
        canvas.AddRequested+=point=>AddCard(CardKind.Note,point);canvas.LinkRequested+=AddLink;
        canvas.ImageDropped+=(path,point)=>ImportImage(path,point);
        autosave.Tick+=(_,_)=>{autosave.Stop();WriteRecovery();};
        KeyDown+=Shortcuts;
        canvas.SetDocument(BoardDocument.Demo());
        Shown+=(_,_)=>{RestoreRecovery();canvas.Fit();canvas.Focus();};
        FormClosing+=(_,e)=>{if(!ConfirmChanges()){e.Cancel=true;return;}autosave.Stop();RemoveRecovery();};
        UpdateInspector();UpdateStatus();
    }
    private void BuildLayout()
    {
        var root=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Margin=Padding.Empty,Padding=Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,88));root.RowStyles.Add(new RowStyle(SizeType.Absolute,61));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,39));Controls.Add(root);
        var header=new Panel{Dock=DockStyle.Fill,BackColor=Theme.Panel,Margin=Padding.Empty};root.Controls.Add(header,0,0);
        var brand=new BrandControl{Location=new(24,22),Size=new(220,42)};header.Controls.Add(brand);
        boardTitle.SetBounds(260,22,550,26);boardTitle.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;
        boardTitle.Cursor=Cursors.Hand;boardTitle.DoubleClick+=(_,_)=>RenameBoard();header.Controls.Add(boardTitle);
        summary.SetBounds(262,53,550,18);summary.ForeColor=Theme.Muted;summary.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;header.Controls.Add(summary);
        var fileActions=new FlowLayoutPanel{Size=new(312,36),Location=new(1064,25),Anchor=AnchorStyles.Right|AnchorStyles.Top,WrapContents=false};header.Controls.Add(fileActions);
        fileActions.Controls.Add(Theme.Button("Новое",NewBoard,false,84));fileActions.Controls.Add(Theme.Button("Открыть",OpenBoard,false,96));fileActions.Controls.Add(Theme.Button("Сохранить",()=>SaveBoard(),true,108));
        header.Resize+=(_,_)=>{fileActions.Left=header.Width-fileActions.Width-24;boardTitle.Width=Math.Max(100,fileActions.Left-boardTitle.Left-20);summary.Width=boardTitle.Width;};
        var tools=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty,Padding=new Padding(24,13,24,12),BackColor=Theme.Background};
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,510));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,96));root.Controls.Add(tools,0,1);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};tools.Controls.Add(buttons,0,0);
        var add=Theme.Button("+ Карточка",()=>{},true,122);add.Click+=(_,_)=>ShowAddMenu(add);buttons.Controls.Add(add);
        buttons.Controls.Add(Theme.Button("Фото",ChooseImage,false,74));buttons.Controls.Add(connectButton);
        buttons.Controls.Add(undoButton);buttons.Controls.Add(redoButton);
        buttons.Controls.Add(Theme.Button("Вся доска",()=>canvas.Fit(),false,110));
        search.PlaceholderText="Поиск по карточкам…";search.AccessibleName="Поиск по карточкам";search.Dock=DockStyle.Fill;search.Margin=new Padding(18,3,14,0);search.MaxLength=200;
        search.TextChanged+=(_,_)=>canvas.Query=search.Text;
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){canvas.LocateMatch();canvas.Focus();e.SuppressKeyPress=true;}};tools.Controls.Add(search,1,0);
        var more=Theme.Button("Ещё ···",()=>{},false,88);more.Click+=(_,_)=>ShowMoreMenu(more);tools.Controls.Add(more,2,0);
        var content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,278));
        root.Controls.Add(content,0,2);canvas.Margin=Padding.Empty;content.Controls.Add(canvas,0,0);
        var inspector=new Panel{Dock=DockStyle.Fill,BackColor=Theme.Panel,Margin=Padding.Empty,AutoScroll=true};content.Controls.Add(inspector,1,0);
        inspector.Paint+=(_,e)=>{using var pen=new Pen(Theme.Border);e.Graphics.DrawLine(pen,0,0,0,inspector.Height);};
        detailType.SetBounds(26,27,226,22);detailType.ForeColor=Theme.Muted;inspector.Controls.Add(detailType);
        detailTitle.SetBounds(24,63,230,100);inspector.Controls.Add(detailTitle);
        detailImage.SetBounds(26,167,224,140);inspector.Controls.Add(detailImage);
        detailBody.SetBounds(26,167,224,200);detailBody.ForeColor=Theme.Muted;inspector.Controls.Add(detailBody);
        detailHint.SetBounds(26,385,224,64);detailHint.ForeColor=Theme.Muted;inspector.Controls.Add(detailHint);
        editButton.SetBounds(26,465,224,35);relateButton.SetBounds(26,510,224,35);deleteButton.SetBounds(26,555,224,35);
        inspector.Controls.AddRange([editButton,relateButton,deleteButton]);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,BackColor=Theme.Panel,Padding=new Padding(24,0,24,0)};
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));root.Controls.Add(footer,0,3);
        status.Dock=DockStyle.Fill;status.TextAlign=ContentAlignment.MiddleLeft;status.ForeColor=Theme.Muted;footer.Controls.Add(status,0,0);
        var zoomPanel=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=new Padding(0,3,0,0),WrapContents=false};footer.Controls.Add(zoomPanel,1,0);
        zoomPanel.Controls.Add(Theme.Button("−",()=>canvas.SetZoom(canvas.Zoom/1.15f),false,34));zoomLabel.Size=new(65,32);zoomLabel.TextAlign=ContentAlignment.MiddleCenter;zoomPanel.Controls.Add(zoomLabel);
        zoomPanel.Controls.Add(Theme.Button("+",()=>canvas.SetZoom(canvas.Zoom*1.15f),false,34));
        var tips=new ToolTip();tips.SetToolTip(boardTitle,"Двойной щелчок — переименовать дело");tips.SetToolTip(undoButton,"Ctrl+Z");tips.SetToolTip(redoButton,"Ctrl+Y");
        Disposed+=(_,_)=>tips.Dispose();
    }
    private void ShowAddMenu(Control anchor)
    {
        var menu=Menu();foreach(var kind in Enum.GetValues<CardKind>())menu.Items.Add(Theme.Kind(kind),null,(_,_)=>AddCard(kind,canvas.CenterWorld));menu.Show(anchor,0,anchor.Height);
    }
    private ContextMenuStrip Menu()
    {var menu=new ContextMenuStrip{BackColor=Theme.Card,ForeColor=Theme.Text,Font=Theme.Font()};menu.Closed+=(_,_)=>menu.Dispose();return menu;}
    private void ShowMoreMenu(Control anchor)
    {
        var menu=Menu();menu.Items.Add("Переименовать дело",null,(_,_)=>RenameBoard());menu.Items.Add("Сохранить как…",null,(_,_)=>SaveBoard(true));
        menu.Items.Add("Экспорт доски в PNG",null,(_,_)=>ExportBoard());menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Открыть учебное дело",null,(_,_)=>{if(ConfirmChanges()){filePath=null;history.Clear();canvas.SetDocument(BoardDocument.Demo(),true);dirty=false;notice="Учебное дело: все персонажи и события вымышлены.";RemoveRecovery();UpdateStatus();}});
        menu.Items.Add("Как пользоваться",null,(_,_)=>MessageBox.Show(this,"Двойной щелчок по доске — новая заметка.\nДвойной щелчок по карточке или связи — редактор.\nПеретаскивайте карточки левой кнопкой мыши.\nПустой фон, средняя кнопка или Пробел + мышь — перемещение.\nКолесо — масштаб. F — показать всю доску.\nКнопка «Связь» — последовательно выбрать две карточки.\nDelete — удалить выбранное. Ctrl+Z / Ctrl+Y — отменить / повторить.\nCtrl+S — сохранить. Ctrl+O — открыть. Ctrl+N — новое дело.\n\nСохранение создаёт переносимый файл .traceboard со всеми фото.","TraceBoard · Справка",MessageBoxButtons.OK,MessageBoxIcon.Information));
        menu.Show(anchor,0,anchor.Height);
    }
    private void UpdateInspector()
    {
        detailImage.Image?.Dispose();detailImage.Image=null;detailImage.Visible=false;
        detailBody.SetBounds(26,167,224,200);detailHint.SetBounds(26,385,224,64);
        var card=canvas.SelectedCard;var link=canvas.SelectedLink;
        editButton.Visible=deleteButton.Visible=card is not null || link is not null;relateButton.Visible=card is not null;
        if(card is not null)
        {
            detailType.Text=Theme.Kind(card.Kind);detailTitle.Text=card.Title;detailBody.Text=card.Body;
            var connected=Document.Links.Where(l=>l.From==card.Id || l.To==card.Id).ToArray();
            detailHint.Text=connected.Length==0?"Связей пока нет.\nОбъедините связанные детали.":$"Связей: {connected.Length}\n{string.Join(" · ",connected.Take(2).Select(l=>l.Label))}";
            if(card.ImageData is not null)
            {
                using var stream=new MemoryStream(Convert.FromBase64String(card.ImageData));using var image=Image.FromStream(stream);detailImage.Image=new Bitmap(image);detailImage.Visible=true;
                detailBody.SetBounds(26,320,224,78);detailHint.SetBounds(26,413,224,39);
            }
        }
        else if(link is not null)
        {
            detailType.Text="СВЯЗЬ";detailTitle.Text=link.Label;
            detailBody.Text=$"{Document.Cards.Find(c=>c.Id==link.From)?.Title}\n\n↕\n\n{Document.Cards.Find(c=>c.Id==link.To)?.Title}";
            detailHint.Text="Двойной щелчок по линии — изменить подпись.";
        }
        else
        {
            detailType.Text="РАБОЧЕЕ ПРОСТРАНСТВО";detailTitle.Text="Соединяйте\nдетали.";
            detailBody.Text="Соберите людей, места, улики и наблюдения на одной доске.\n\nВыберите карточку, чтобы посмотреть подробности.\n\nПеретаскивайте фотографии прямо из проводника.";
            detailHint.Text="Колесо — масштаб\nПробел + мышь — движение\nДвойной щелчок — редактор";
        }
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        boardTitle.Text=Document.Title+(dirty?"  •":"");Text=$"{Document.Title} — TraceBoard";
        summary.Text=$"{Document.Cards.Count} карточек   /   {Document.Links.Count} связей   /   {(filePath is null?"локальная доска":Path.GetFileName(filePath))}";
        status.Text=canvas.Query.Length>0?$"Найдено: {canvas.MatchCount} · Enter — перейти к карточке":notice.Length>0?notice:"Двойной щелчок — редактор   ·   Пробел + мышь — движение   ·   Колесо — масштаб";
        zoomLabel.Text=$"{canvas.Zoom*100:0}%";undoButton.Enabled=history.CanUndo;redoButton.Enabled=history.CanRedo;
        connectButton.BackColor=canvas.LinkMode?Theme.Text:Theme.Card;connectButton.ForeColor=canvas.LinkMode?Theme.Background:Theme.Text;
    }
    private void Changed()
    {dirty=true;notice="Изменения в черновике · Ctrl+S — сохранить дело";autosave.Stop();autosave.Start();canvas.RefreshContent();}
    private void Change(Action action,bool clearImages=false)
    {string before=BoardFiles.Serialize(Document);action();history.Record(before);if(clearImages)canvas.RefreshContent(true);Changed();}
    private void AddCard(CardKind kind,PointF point)
    {
        if(Document.Cards.Count>=500){Notice("На доске уже 500 карточек.");return;}
        var card=new BoardCard{Kind=kind,Title="",X=Math.Clamp(point.X-122,-90000,90000),Y=Math.Clamp(point.Y-70,-90000,90000)};
        using var dialog=new CardDialog(card,true);if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        card.Title=dialog.CardTitle;card.Body=dialog.CardBody;card.Kind=dialog.Kind;card.ImageData=dialog.ImageData;
        Change(()=>Document.Cards.Add(card));canvas.SelectCard(card.Id);canvas.Focus();
    }
    private void EditSelected()
    {
        if(canvas.SelectedCard is { } card)
        {
            using var dialog=new CardDialog(card,false);if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            if(card.Title==dialog.CardTitle && card.Body==dialog.CardBody && card.Kind==dialog.Kind && card.ImageData==dialog.ImageData)return;
            Change(()=>{card.Title=dialog.CardTitle;card.Body=dialog.CardBody;card.Kind=dialog.Kind;card.ImageData=dialog.ImageData;},true);
        }
        else if(canvas.SelectedLink is { } link)
        {string? label=TextDialog.Ask(this,"Подпись связи","Как связаны эти карточки?",link.Label);if(label is not null && label!=link.Label)Change(()=>link.Label=label);}
        canvas.Focus();
    }
    private void DeleteSelected()
    {
        if(canvas.SelectedCard is { } card)Change(()=>{Document.Cards.Remove(card);Document.Links.RemoveAll(l=>l.From==card.Id || l.To==card.Id);},true);
        else if(canvas.SelectedLink is { } link)Change(()=>Document.Links.Remove(link));
        canvas.SelectCard(null);canvas.Focus();
    }
    private void AddLink(string from,string to)
    {
        if(Document.Links.Count>=2000){Notice("Достигнут лимит связей.");return;}
        if(Document.Links.Any(l=>l.From==from && l.To==to || l.From==to && l.To==from)){Notice("Эти карточки уже связаны. Дважды щёлкните по линии, чтобы изменить подпись.");return;}
        string? label=TextDialog.Ask(this,"Новая связь","Как связаны эти карточки?","связь");if(label is null)return;
        Change(()=>Document.Links.Add(new(){From=from,To=to,Label=label}));canvas.Focus();
    }
    private void ChooseImage()
    {
        using var picker=new OpenFileDialog{Title="Добавить фото на доску",Filter="Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif"};
        if(picker.ShowDialog(this)==DialogResult.OK)ImportImage(picker.FileName,canvas.CenterWorld);
    }
    private void ImportImage(string path,PointF point)
    {
        if(Document.Cards.Count>=500){Notice("На доске уже 500 карточек.");return;}
        try
        {
            string title=Path.GetFileNameWithoutExtension(path);title=title[..Math.Min(120,title.Length)];
            var card=new BoardCard{Kind=CardKind.Evidence,Title=title,ImageData=PhotoImport.Read(path),X=Math.Clamp(point.X-122,-90000,90000),Y=Math.Clamp(point.Y-100,-90000,90000)};
            Change(()=>Document.Cards.Add(card));canvas.SelectCard(card.Id);
        }
        catch(Exception ex)when(ex is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException){ShowError("Не удалось добавить фото",ex);}
    }
    private void RenameBoard()
    {string? title=TextDialog.Ask(this,"Название расследования","Название дела",Document.Title,120);if(title is not null && title!=Document.Title)Change(()=>Document.Title=title);}
    private void Undo()
    {if(history.Undo(Document) is { } doc){canvas.SetDocument(doc);Changed();}}
    private void Redo()
    {if(history.Redo(Document) is { } doc){canvas.SetDocument(doc);Changed();}}
    private void NewBoard()
    {
        if(!ConfirmChanges())return;
        string? title=TextDialog.Ask(this,"Новое расследование","Название дела","Новое расследование",120);if(title is null)return;
        filePath=null;history.Clear();canvas.SetDocument(new(){Title=title},true);search.Clear();Changed();canvas.Focus();
    }
    private void OpenBoard()
    {
        if(!ConfirmChanges())return;
        using var picker=new OpenFileDialog{Filter="Доска TraceBoard|*.traceboard|JSON|*.json",Title="Открыть расследование"};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        try
        {
            var loaded=BoardFiles.Read(picker.FileName);filePath=picker.FileName;history.Clear();canvas.SetDocument(loaded,true);
            dirty=false;search.Clear();notice="Дело открыто";autosave.Stop();RemoveRecovery();UpdateStatus();canvas.Focus();
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){ShowError("Не удалось открыть доску",ex);}
    }
    private bool SaveBoard(bool saveAs=false)
    {
        string? destination=filePath;
        if(destination is null || saveAs)
        {
            using var picker=new SaveFileDialog{Filter="Доска TraceBoard|*.traceboard",DefaultExt="traceboard",AddExtension=true,FileName=SafeName(Document.Title)+".traceboard",InitialDirectory=Path.GetDirectoryName(filePath)??AppContext.BaseDirectory};
            if(picker.ShowDialog(this)!=DialogResult.OK)return false;destination=picker.FileName;
        }
        try
        {
            saving=true;BoardFiles.Write(destination,Document);filePath=destination;dirty=false;autosave.Stop();RemoveRecovery();notice="Сохранено · "+Path.GetFileName(destination);UpdateStatus();return true;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){ShowError("Не удалось сохранить доску",ex);return false;}
        finally{saving=false;}
    }
    private bool ConfirmChanges()
    {
        if(!dirty)return true;
        var answer=MessageBox.Show(this,"Сохранить изменения в текущем расследовании?","TraceBoard",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
        return answer==DialogResult.No || answer==DialogResult.Yes && SaveBoard();
    }
    private void ExportBoard()
    {
        using var picker=new SaveFileDialog{Filter="Изображение PNG|*.png",DefaultExt="png",FileName=SafeName(Document.Title)+".png",InitialDirectory=Path.GetDirectoryName(filePath)??AppContext.BaseDirectory};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        try{using var image=canvas.Export();image.Save(picker.FileName,ImageFormat.Png);Notice("Доска экспортирована в PNG");}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException){ShowError("Не удалось экспортировать доску",ex);}
    }
    private void WriteRecovery()
    {
        if(!dirty || saving)return;
        try{Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);BoardFiles.Write(recovery,Document);notice="Черновик сохранён локально · Ctrl+S — сохранить файл дела";UpdateStatus();}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Notice("Не удалось сохранить черновик. Используйте Ctrl+S.");}
    }
    private void RestoreRecovery()
    {
        if(!File.Exists(recovery))return;
        if(MessageBox.Show(this,"Найден черновик после предыдущего запуска. Восстановить его?","TraceBoard",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes){RemoveRecovery();return;}
        try{canvas.SetDocument(BoardFiles.Read(recovery));dirty=true;notice="Черновик восстановлен · Ctrl+S — сохранить дело";UpdateStatus();}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){ShowError("Не удалось восстановить черновик",ex);}
    }
    private void RemoveRecovery(){try{if(File.Exists(recovery))File.Delete(recovery);}catch(IOException){}catch(UnauthorizedAccessException){}}
    private void Notice(string message){notice=message;UpdateStatus();}
    private void ShowError(string title,Exception ex)=>MessageBox.Show(this,ex.Message,title,MessageBoxButtons.OK,MessageBoxIcon.Warning);
    private static string SafeName(string text)=>string.Concat(text.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
    private void Shortcuts(object? sender,KeyEventArgs e)
    {
        if(e.Control && e.KeyCode==Keys.S){SaveBoard(e.Shift);e.SuppressKeyPress=true;return;}
        if(e.Control && e.KeyCode==Keys.O){OpenBoard();e.SuppressKeyPress=true;return;}
        if(e.Control && e.KeyCode==Keys.N){NewBoard();e.SuppressKeyPress=true;return;}
        if(search.Focused)return;
        if(e.Control && e.KeyCode==Keys.Z){Undo();e.SuppressKeyPress=true;}
        else if(e.Control && e.KeyCode==Keys.Y){Redo();e.SuppressKeyPress=true;}
        else if(e.KeyCode==Keys.Delete){DeleteSelected();e.SuppressKeyPress=true;}
        else if(e.KeyCode==Keys.F && !e.Control){canvas.Fit();e.SuppressKeyPress=true;}
        else if(e.KeyCode==Keys.Escape){canvas.LinkMode=false;canvas.Focus();e.SuppressKeyPress=true;}
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);int dark=1;DwmSetWindowAttribute(Handle,20,ref dark,sizeof(int));}
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr handle,int attr,ref int value,int size);
    protected override void Dispose(bool disposing){if(disposing){autosave.Dispose();detailImage.Image?.Dispose();}base.Dispose(disposing);}
}

public sealed class BrandControl : Control
{
    public BrandControl(){DoubleBuffered=true;}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.ScaleTransform(DeviceDpi/96f,DeviceDpi/96f);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var line=new Pen(Theme.Text,1.7f);g.DrawLine(line,7,9,29,20);g.DrawLine(line,7,32,29,20);g.DrawLine(line,7,9,7,32);
        using var ink=new SolidBrush(Theme.Text);foreach(var point in new[]{new Point(7,9),new Point(7,32),new Point(29,20)})g.FillEllipse(ink,point.X-4,point.Y-4,8,8);
        Theme.DrawText(g,"TraceBoard",new(45,7,175,34),23,Theme.Text,true);
    }
}
