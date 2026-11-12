using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace GraficaCaixa.Wpf;

// Catálogo para WhatsApp: páginas A4 por categoria com a foto Principal, o nome e o preço de venda (custo e margem nunca saem).
// Cada página vira uma imagem JPG (para mandar no WhatsApp) e todas juntas formam um PDF.
public static class WhatsAppCatalog
{
    public const double PageWidth=794,PageHeight=1123;public const int Columns=3,Rows=4,PerPage=Columns*Rows;const double Dpi=192;
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    static readonly Brush Ink=Frozen("#202A30"),Muted=Frozen("#5B6970"),Accent=Frozen("#147A70"),AccentSoft=Frozen("#E3F1EE"),Line=Frozen("#DCE5E2"),Soft=Frozen("#F2F6F5");
    static Brush Frozen(string hex){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));brush.Freeze();return brush;}
    public sealed record Entry(Product Product,BitmapSource? Photo);

    // Preço do catálogo: o mesmo da tabela de venda; tabela por faixa mostra "a partir de"; sem preço = "Sob consulta".
    public static (string Price,string Basis) Price(Product p)
    {
        var unit=string.IsNullOrWhiteSpace(p.Unit)?"un":p.Unit.Trim();
        if(QuantityPricing.HasExactTable(p)){var first=p.ExactQuantityPrices.Where(x=>x.Quantity!=1).OrderBy(x=>x.Quantity).First();return (first.TotalPrice.ToString("C2",Pt),string.Format(Pt,"por {0:N0} {1}",first.Quantity,unit));}
        if(p.UseQuantityPricing&&p.QuantityPriceTiers.Count>0)return ("a partir de "+p.QuantityPriceTiers.Min(x=>x.UnitPrice).ToString("C2",Pt),$"por {unit}, conforme a quantidade");
        return QuantityPricing.BasePrice(p) is decimal price&&price>0?(price.ToString("C2",Pt),$"por {unit}"):("Sob consulta","");
    }

    // Com foto: cartões 3 × 4. Sem foto: lista compacta "Outros itens" (nome e preço), no espaço que sobra e nas páginas seguintes.
    const double BodyHeight=900,CardHeight=222,RowHeight=28,ListHeader=38;
    public static List<FrameworkElement> Pages(CompanyProfile company,BitmapSource? logo,string category,IReadOnlyList<Entry> entries,DateTime date)
    {
        var plan=new List<(List<Entry> Cards,List<Entry> Rows)>();var cards=entries.Where(x=>x.Photo!=null).ToList();var rows=new Queue<Entry>(entries.Where(x=>x.Photo==null));
        for(var at=0;at<cards.Count;at+=PerPage)
        {
            var chunk=cards.Skip(at).Take(PerPage).ToList();var free=BodyHeight-Math.Ceiling(chunk.Count/(double)Columns)*CardHeight-ListHeader;
            var fit=at+PerPage>=cards.Count?Math.Max(0,(int)(free/RowHeight)):0;var list=new List<Entry>();while(fit-->0&&rows.Count>0)list.Add(rows.Dequeue());plan.Add((chunk,list));
        }
        while(rows.Count>0){var list=new List<Entry>();for(var fit=(int)((BodyHeight-ListHeader)/RowHeight);fit>0&&rows.Count>0;fit--)list.Add(rows.Dequeue());plan.Add(([],list));}
        if(plan.Count==0)plan.Add(([],[]));
        return plan.Select((x,i)=>Page(company,logo,category,x.Cards,x.Rows,date,i+1,plan.Count)).ToList();
    }

    static FrameworkElement Page(CompanyProfile company,BitmapSource? logo,string category,List<Entry> cards,List<Entry> rows,DateTime date,int number,int total)
    {
        var root=new Grid{Width=PageWidth,Height=PageHeight,Background=Brushes.White};
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        // Cabeçalho: logo + empresa à esquerda, categoria à direita, faixa verde embaixo.
        var header=new DockPanel{Margin=new Thickness(40,32,40,0)};
        var right=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(right,Dock.Right);
        right.Children.Add(new TextBlock{Text=category,FontFamily=new FontFamily("Segoe UI Semibold"),FontSize=26,Foreground=Ink,TextAlignment=TextAlignment.Right,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=420});
        right.Children.Add(new TextBlock{Text=$"Catálogo · {date.ToString("MMMM 'de' yyyy",Pt)}"+(total>1?$" · {number}/{total}":""),FontSize=12,Foreground=Muted,TextAlignment=TextAlignment.Right,Margin=new Thickness(0,2,0,0)});
        header.Children.Add(right);
        var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        if(logo!=null)brand.Children.Add(new Image{Source=logo,Height=58,MaxWidth=150,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,14,0)});
        var who=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        who.Children.Add(new TextBlock{Text=company.Name,FontFamily=new FontFamily("Segoe UI Semibold"),FontSize=15,Foreground=Ink,MaxWidth=230,TextTrimming=TextTrimming.CharacterEllipsis});
        if(!string.IsNullOrWhiteSpace(company.Phone))who.Children.Add(new TextBlock{Text="WhatsApp "+company.Phone,FontSize=12,Foreground=Muted,Margin=new Thickness(0,2,0,0)});
        brand.Children.Add(who);header.Children.Add(brand);
        var top=new StackPanel{Children={header,new Border{Height=3,Background=Accent,CornerRadius=new CornerRadius(2),Margin=new Thickness(40,16,40,0)}}};root.Children.Add(top);
        var body=new StackPanel{Margin=new Thickness(34,14,34,0)};Grid.SetRow(body,1);
        if(cards.Count>0){var grid=new System.Windows.Controls.Primitives.UniformGrid{Columns=Columns,VerticalAlignment=VerticalAlignment.Top};foreach(var entry in cards)grid.Children.Add(Card(entry));body.Children.Add(grid);}
        if(rows.Count>0)
        {
            body.Children.Add(new TextBlock{Text=cards.Count>0?"Outros itens":"Itens",FontFamily=new FontFamily("Segoe UI Semibold"),FontSize=15,Foreground=Ink,Margin=new Thickness(6,cards.Count>0?12:4,6,6)});
            foreach(var entry in rows)body.Children.Add(Row(entry));
        }
        root.Children.Add(body);
        // Rodapé: endereço e contato.
        var contact=string.Join("  ·  ",new[]{company.Address+(string.IsNullOrWhiteSpace(company.City)?"":" - "+company.City),company.Phone,company.Email}.Where(x=>!string.IsNullOrWhiteSpace(x)&&x.Trim()!="-"));
        var footer=new StackPanel{Margin=new Thickness(40,0,40,22)};Grid.SetRow(footer,2);
        footer.Children.Add(new Border{Height=1,Background=Line,Margin=new Thickness(0,0,0,8)});
        footer.Children.Add(new TextBlock{Text=contact,FontSize=11,Foreground=Muted,TextAlignment=TextAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});
        footer.Children.Add(new TextBlock{Text="Preços sujeitos a alteração sem aviso. Peça seu orçamento pelo WhatsApp.",FontSize=10,Foreground=Muted,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,2,0,0)});
        root.Children.Add(footer);
        return root;
    }

    static FrameworkElement Card(Entry entry)
    {
        var (price,basis)=Price(entry.Product);
        var stack=new StackPanel();
        stack.Children.Add(new Border{Height=120,CornerRadius=new CornerRadius(10),Background=new ImageBrush(entry.Photo){Stretch=Stretch.Uniform}});
        stack.Children.Add(new TextBlock{Text=entry.Product.Description,FontFamily=new FontFamily("Segoe UI Semibold"),FontSize=13,Foreground=Ink,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,Height=34,Margin=new Thickness(2,8,2,0),LineHeight=17,LineStackingStrategy=LineStackingStrategy.BlockLineHeight});
        stack.Children.Add(PriceLine(price,basis,new Thickness(2,6,2,0)));
        return new Border{Height=CardHeight-12,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(8),Margin=new Thickness(6),Background=Brushes.White,Child=stack};
    }

    static FrameworkElement Row(Entry entry)
    {
        var (price,basis)=Price(entry.Product);
        var row=new DockPanel{Height=RowHeight,Margin=new Thickness(6,0,6,0)};
        var right=PriceLine(price,basis,new Thickness(12,0,0,0));right.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(right,Dock.Right);row.Children.Add(right);
        row.Children.Add(new TextBlock{Text=entry.Product.Description,FontSize=12.5,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});
        return new Border{BorderBrush=Line,BorderThickness=new Thickness(0,0,0,1),Child=row};
    }

    static FrameworkElement PriceLine(string price,string basis,Thickness margin)
    {
        var line=new StackPanel{Orientation=Orientation.Horizontal,Margin=margin};
        line.Children.Add(new Border{Background=AccentSoft,CornerRadius=new CornerRadius(6),Padding=new Thickness(7,0,7,1),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text=price,FontFamily=new FontFamily("Segoe UI Semibold"),FontSize=14,Foreground=Accent}});
        if(basis.Length>0)line.Children.Add(new TextBlock{Text=basis,FontSize=10.5,Foreground=Muted,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,0,0)});
        return line;
    }
    // Página → JPG em 2× (1588 × 2246 px): nítida no celular e leve para o WhatsApp.
    public static (byte[] Jpeg,int Width,int Height) Render(FrameworkElement page)
    {
        page.Measure(new Size(PageWidth,PageHeight));page.Arrange(new Rect(0,0,PageWidth,PageHeight));page.UpdateLayout();
        int width=(int)Math.Round(PageWidth*Dpi/96),height=(int)Math.Round(PageHeight*Dpi/96);
        var bitmap=new RenderTargetBitmap(width,height,Dpi,Dpi,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,PageWidth,PageHeight));dc.DrawRectangle(new VisualBrush(page),null,new Rect(0,0,PageWidth,PageHeight));}
        bitmap.Render(visual);bitmap.Freeze();
        var encoder=new JpegBitmapEncoder{QualityLevel=88};encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);return (output.ToArray(),width,height);
    }

    // PDF A4 com uma imagem JPG por página (DCTDecode): sem biblioteca externa.
    public static void WritePdf(IReadOnlyList<(byte[] Jpeg,int Width,int Height)> pages,string path)
    {
        if(pages.Count==0)throw new InvalidOperationException("Nenhuma página para o catálogo.");
        var enc=Encoding.Latin1;var objects=new List<byte[]>();const string w="595.28",h="841.89";
        byte[] Stream(string dictionary,byte[] data){using var s=new MemoryStream();s.Write(enc.GetBytes($"<< {dictionary} /Length {data.Length} >>\nstream\n"));s.Write(data);s.Write(enc.GetBytes("\nendstream"));return s.ToArray();}
        objects.Add(enc.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(enc.GetBytes($"<< /Type /Pages /Kids [{string.Join(" ",Enumerable.Range(0,pages.Count).Select(i=>$"{3+3*i} 0 R"))}] /Count {pages.Count} >>"));
        for(var i=0;i<pages.Count;i++)
        {
            objects.Add(enc.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /P {5+3*i} 0 R >> >> /Contents {4+3*i} 0 R >>"));
            objects.Add(Stream("",enc.GetBytes($"q {w} 0 0 {h} 0 0 cm /P Do Q")));
            objects.Add(Stream($"/Type /XObject /Subtype /Image /Width {pages[i].Width} /Height {pages[i].Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode",pages[i].Jpeg));
        }
        using var file=new MemoryStream();file.Write(enc.GetBytes("%PDF-1.4\n%âãÏÓ\n"));var offsets=new List<long>();
        for(var i=0;i<objects.Count;i++){offsets.Add(file.Position);file.Write(enc.GetBytes($"{i+1} 0 obj\n"));file.Write(objects[i]);file.Write(enc.GetBytes("\nendobj\n"));}
        var xref=file.Position;var table=new StringBuilder($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n");foreach(var offset in offsets)table.Append(offset.ToString("D10",CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        table.Append($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");file.Write(enc.GetBytes(table.ToString()));
        File.WriteAllBytes(path,file.ToArray());
    }

    public static string FileName(string value)
    {
        var name=new string(value.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?' ':c).ToArray()).Trim();
        return string.Join(' ',name.Split(' ',StringSplitOptions.RemoveEmptyEntries)) is {Length:>0} clean?clean[..Math.Min(60,clean.Length)].TrimEnd('.',' '):"Categoria";
    }
}

// Janela "Catálogo": escolher categorias e gerar PDF + imagens em Documentos\Catálogos do ERP.
public sealed class CatalogWindow:Window
{
    readonly DataStore store;readonly List<Product> products;readonly StackPanel list=new();readonly CheckBox onlyPhotos=new(){Content="Só produtos com foto",Margin=new Thickness(0,12,0,0)};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};readonly Button generate=new(){Content="Gerar catálogo"};
    public string? OutputFolder{get;private set;}
    public CatalogWindow(DataStore store,List<Product> products)
    {
        this.store=store;this.products=products.Where(x=>x.Active).ToList();
        Title="Catálogo para WhatsApp";Width=Math.Min(560,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
        status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        foreach(var group in this.products.GroupBy(x=>string.IsNullOrWhiteSpace(x.Category)?"Sem categoria":x.Category.Trim()).OrderBy(x=>x.Key,StringComparer.CurrentCultureIgnoreCase))
        {
            var photos=group.Count(x=>!string.IsNullOrWhiteSpace(x.PhotoPath));
            list.Children.Add(new CheckBox{Content=$"{group.Key}  ·  {group.Count()} produto(s), {photos} com foto",Tag=group.Key,IsChecked=photos>0,Margin=new Thickness(0,3,0,3)});
        }
        var all=new Button{Content="Marcar todas",Style=(Style)FindResource("GhostButton")};all.Click+=(_,_)=>Mark(true);
        var none=new Button{Content="Desmarcar",Style=(Style)FindResource("GhostButton"),Margin=new Thickness(6,0,0,0)};none.Click+=(_,_)=>Mark(false);
        var body=new StackPanel{Children={
            new TextBlock{Text="Por categoria: produtos com foto em cartões (12 por página) e os sem foto numa lista com nome e preço. Custo e margem não aparecem.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)},
            new DockPanel{Children={new StackPanel{Orientation=Orientation.Horizontal,Children={all,none}}},Margin=new Thickness(0,0,0,6)},
            new Border{Style=(Style)FindResource("Card"),Padding=new Thickness(14,8,14,8),Child=new ScrollViewer{MaxHeight=320,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=list}},
            onlyPhotos,status}};
        generate.Style=(Style)FindResource("PrimaryButton");generate.Margin=new Thickness(8,0,0,0);generate.Click+=async(_,_)=>await GenerateAsync();
        var cancel=new Button{Content="Fechar",Style=(Style)FindResource("GhostButton")};cancel.Click+=(_,_)=>Close();
        Content=new DialogFrame{Kind="Neutro",IconName="image",Title="Catálogo para WhatsApp",Subtitle="PDF e uma imagem por página, prontos para enviar",Content=body,Footer=new StackPanel{Orientation=Orientation.Horizontal,Children={cancel,generate}},ScrollBody=false};
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;Close();}};
    }
    void Mark(bool value){foreach(var box in list.Children.OfType<CheckBox>())box.IsChecked=value;}

    async Task GenerateAsync()
    {
        var categories=list.Children.OfType<CheckBox>().Where(x=>x.IsChecked==true).Select(x=>(string)x.Tag).ToList();
        if(categories.Count==0){status.Text="Marque pelo menos uma categoria.";return;}
        generate.IsEnabled=false;
        try
        {
            var chosen=products.Where(x=>categories.Contains(string.IsNullOrWhiteSpace(x.Category)?"Sem categoria":x.Category.Trim())&&(onlyPhotos.IsChecked!=true||!string.IsNullOrWhiteSpace(x.PhotoPath))).ToList();
            if(chosen.Count==0){status.Text="Nenhum produto nas categorias marcadas.";return;}
            var photos=new Dictionary<Guid,BitmapSource?>();var done=0;
            using var gate=new SemaphoreSlim(3);
            await Task.WhenAll(chosen.Where(x=>!string.IsNullOrWhiteSpace(x.PhotoPath)).Select(async p=>{await gate.WaitAsync();try{photos[p.Id]=await ProductPhotos.LoadAsync(store,p.Id,0,p.PhotoPath!,600);}catch(Exception ex){System.Diagnostics.Trace.WriteLine("Foto do catálogo indisponível: "+ex.Message);}finally{gate.Release();status.Text=$"Carregando fotos… {++done}";}}));
            var (company,logo)=CompanyAndLogo(store);
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Catálogos do ERP",$"Catálogo {DateTime.Now:yyyy-MM-dd HH'h'mm}");Directory.CreateDirectory(folder);
            var rendered=new List<(byte[] Jpeg,int Width,int Height)>();var index=0;
            foreach(var category in categories)
            {
                var entries=chosen.Where(x=>(string.IsNullOrWhiteSpace(x.Category)?"Sem categoria":x.Category.Trim())==category).OrderBy(x=>x.Description,StringComparer.CurrentCultureIgnoreCase).Select(x=>new WhatsAppCatalog.Entry(x,photos.GetValueOrDefault(x.Id))).ToList();
                if(entries.Count==0)continue;
                var pages=WhatsAppCatalog.Pages(company,logo,category,entries,DateTime.Today);
                for(var i=0;i<pages.Count;i++)
                {
                    status.Text=$"Montando {category}…";await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                    var page=WhatsAppCatalog.Render(pages[i]);rendered.Add(page);index++;
                    await File.WriteAllBytesAsync(Path.Combine(folder,$"{index:00} {WhatsAppCatalog.FileName(category)}{(pages.Count>1?$" ({i+1} de {pages.Count})":"")}.jpg"),page.Jpeg);
                }
            }
            WhatsAppCatalog.WritePdf(rendered,Path.Combine(folder,"Catálogo.pdf"));
            OutputFolder=folder;status.Text=$"Pronto: {rendered.Count} página(s) em {folder}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",$"\"{folder}\""){UseShellExecute=true});
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException){status.Text="Não foi possível gerar o catálogo: "+ex.Message;}
        finally{generate.IsEnabled=true;}
    }

    // Empresa e logotipo do cadastro (Sistema › Empresa). Sem acesso à pasta do logo (ex.: notebook), usa o logotipo do programa.
    public static (CompanyProfile Company,BitmapSource? Logo) CompanyAndLogo(DataStore store)
    {
        var data=store.IsRemote?DocumentPaths.Bootstrap:store.Load();var company=data?.Company??new CompanyProfile();
        BitmapSource? logo=null;
        try{var path=data==null?"":DocumentPaths.ResolveStoredPath(data.DocumentsRoot,data.LogoPath);if(!string.IsNullOrWhiteSpace(path)&&File.Exists(path))logo=ProductPhotos.Read(path,400);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException){}
        if(logo==null)try{using var stream=typeof(ReportWriter).Assembly.GetManifestResourceStream("GraficaCaixa.OrderLogo.png");if(stream!=null){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();logo=image;}}catch(Exception ex) when(ex is IOException or NotSupportedException){}
        return (company,logo);
    }

    public static async Task<List<Product>> LoadProductsAsync(DataStore store)
    {
        if(!store.IsRemote)return store.Load().Products;
        var all=new List<Product>();
        for(var page=1;page<=50;page++)
        {
            var result=await store.QueryRemoteAsync<ModulePage>($"api/v1/produtos?projection=desktop&page={page}&pageSize=100");
            all.AddRange(System.Text.Json.JsonSerializer.Deserialize<AppData>(result.Data)?.Products??[]);
            if((long)page*result.PageSize>=result.Total)break;
        }
        return all;
    }
}
