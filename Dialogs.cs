using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TraceBoard;

public class DarkDialog : Form
{
    public DarkDialog(string title,Size size)
    {
        Text=title;ClientSize=size;BackColor=Theme.Panel;ForeColor=Theme.Text;Font=Theme.Font();
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
        StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.Dpi;ShowInTaskbar=false;
    }
    protected override void OnHandleCreated(EventArgs e)
    {base.OnHandleCreated(e);int value=1;DwmSetWindowAttribute(Handle,20,ref value,sizeof(int));}
    [DllImport("dwmapi.dll")]private static extern int DwmSetWindowAttribute(IntPtr handle,int attr,ref int value,int size);
}

public sealed class TextDialog : DarkDialog
{
    private readonly TextBox input=Theme.Input();
    public string Value=>input.Text.Trim();
    public TextDialog(string title,string prompt,string value,int limit=100):base(title,new(460,195))
    {
        var label=Theme.Label(prompt,13);label.SetBounds(24,20,412,38);Controls.Add(label);
        input.SetBounds(24,67,412,30);input.MaxLength=limit;input.Text=value;Controls.Add(input);
        var ok=Theme.Button("Готово",()=>{if(Value.Length==0){input.Focus();return;}DialogResult=DialogResult.OK;},true);
        ok.SetBounds(212,131,108,36);Controls.Add(ok);
        var cancel=Theme.Button("Отмена",()=>DialogResult=DialogResult.Cancel);cancel.SetBounds(328,131,108,36);Controls.Add(cancel);
        AcceptButton=ok;CancelButton=cancel;Shown+=(_,_)=>{input.Focus();input.SelectAll();};
    }
    public static string? Ask(IWin32Window owner,string title,string prompt,string value,int limit=100)
    {using var dialog=new TextDialog(title,prompt,value,limit);return dialog.ShowDialog(owner)==DialogResult.OK?dialog.Value:null;}
}

public sealed class CardDialog : DarkDialog
{
    private readonly TextBox titleBox=Theme.Input();
    private readonly TextBox bodyBox=Theme.Input(true);
    private readonly ComboBox kindBox=new();
    private readonly PictureBox preview=new();
    private readonly Label photoLabel=Theme.Label("Фотография не добавлена",11);
    private string? imageData;
    public string CardTitle=>titleBox.Text.Trim();
    public string CardBody=>bodyBox.Text.Trim();
    public CardKind Kind=>(CardKind)Math.Max(0,kindBox.SelectedIndex);
    public string? ImageData=>imageData;
    public CardDialog(BoardCard card,bool isNew):base(isNew?"Добавить карточку":"Редактировать карточку",new(570,650))
    {
        AddLabel("КАРТОЧКА",24,20,10,true);
        AddLabel("Название",24,54,12);titleBox.SetBounds(24,78,522,32);titleBox.MaxLength=120;titleBox.Text=card.Title;Controls.Add(titleBox);
        AddLabel("Тип",24,126,12);
        kindBox.SetBounds(24,150,522,32);kindBox.DropDownStyle=ComboBoxStyle.DropDownList;kindBox.BackColor=Theme.Card;kindBox.ForeColor=Theme.Text;kindBox.Font=Theme.Font(14);
        kindBox.Items.AddRange(new object[]{"Персона","Улика","Место","Заметка"});kindBox.SelectedIndex=(int)card.Kind;Controls.Add(kindBox);
        AddLabel("Описание и наблюдения",24,197,12);bodyBox.SetBounds(24,221,522,172);bodyBox.MaxLength=20000;bodyBox.Text=card.Body;bodyBox.AcceptsReturn=true;Controls.Add(bodyBox);
        preview.SetBounds(24,414,120,100);preview.SizeMode=PictureBoxSizeMode.Zoom;preview.BackColor=Theme.Background;Controls.Add(preview);
        photoLabel.SetBounds(160,414,386,29);photoLabel.ForeColor=Theme.Muted;Controls.Add(photoLabel);
        var add=Theme.Button("Добавить фото",ChoosePhoto,false,170);add.SetBounds(160,453,174,34);Controls.Add(add);
        var remove=Theme.Button("Убрать",()=>{imageData=null;UpdatePreview();},false,96);remove.SetBounds(344,453,110,34);Controls.Add(remove);
        var note=Theme.Label("PNG, JPG, BMP, GIF · до 20 МБ\nФото сохраняется внутри файла доски.",11);note.ForeColor=Theme.Muted;note.SetBounds(24,531,522,40);Controls.Add(note);
        var save=Theme.Button(isNew?"Добавить":"Сохранить",()=>{if(CardTitle.Length==0){titleBox.Focus();return;}DialogResult=DialogResult.OK;},true,128);save.SetBounds(278,588,128,36);Controls.Add(save);
        var cancel=Theme.Button("Отмена",()=>DialogResult=DialogResult.Cancel,false,128);cancel.SetBounds(418,588,128,36);Controls.Add(cancel);
        CancelButton=cancel;imageData=card.ImageData;UpdatePreview();Shown+=(_,_)=>{titleBox.Focus();titleBox.SelectAll();};
    }
    private void AddLabel(string text,int x,int y,int size,bool bold=false){var l=Theme.Label(text,size,bold);l.SetBounds(x,y,510,24);Controls.Add(l);}
    private void ChoosePhoto()
    {
        using var picker=new OpenFileDialog{Title="Выбрать фотографию",Filter="Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы|*.*"};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        try{imageData=PhotoImport.Read(picker.FileName);UpdatePreview();}
        catch(Exception ex)when(ex is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException){MessageBox.Show(this,ex.Message,"Не удалось открыть фото",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    private void UpdatePreview()
    {
        preview.Image?.Dispose();preview.Image=null;
        if(imageData is null){photoLabel.Text="Фотография не добавлена";return;}
        using var stream=new MemoryStream(Convert.FromBase64String(imageData));using var source=Image.FromStream(stream);preview.Image=new Bitmap(source);photoLabel.Text=$"Фото · {source.Width} × {source.Height}";
    }
    protected override void Dispose(bool disposing){if(disposing)preview.Image?.Dispose();base.Dispose(disposing);}
}

public static class PhotoImport
{
    public static string Read(string path)
    {
        if(new FileInfo(path).Length>20_000_000)throw new InvalidDataException("Выберите фотографию размером до 20 МБ.");
        using var source=Image.FromFile(path);
        if(source.Width>12000 || source.Height>12000)throw new InvalidDataException("Сторона изображения не должна превышать 12 000 пикселей.");
        // Respect the EXIF orientation before embedding a portable copy.
        try
        {
            if(source.GetPropertyItem(0x112)?.Value is byte[] orientation && orientation.Length>=2)
                source.RotateFlip(BitConverter.ToUInt16(orientation,0) switch
                {
                    2=>RotateFlipType.RotateNoneFlipX,3=>RotateFlipType.Rotate180FlipNone,4=>RotateFlipType.Rotate180FlipX,
                    5=>RotateFlipType.Rotate90FlipX,6=>RotateFlipType.Rotate90FlipNone,7=>RotateFlipType.Rotate270FlipX,
                    8=>RotateFlipType.Rotate270FlipNone,_=>RotateFlipType.RotateNoneFlipNone
                });
        }
        catch(ArgumentException){ }
        float factor=Math.Min(1,1600f/Math.Max(source.Width,source.Height));
        using var resized=new Bitmap(Math.Max(1,(int)(source.Width*factor)),Math.Max(1,(int)(source.Height*factor)));
        using(var g=Graphics.FromImage(resized)){g.Clear(Color.FromArgb(24,24,24));g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(source,0,0,resized.Width,resized.Height);}
        using var stream=new MemoryStream();resized.Save(stream,ImageFormat.Jpeg);return Convert.ToBase64String(stream.ToArray());
    }
}

