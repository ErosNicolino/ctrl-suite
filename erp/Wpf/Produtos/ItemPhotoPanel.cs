using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
namespace GraficaCaixa.Wpf;

// "Item em foco" na lateral do Novo lançamento e do Pedido: a foto Principal do item em que a pessoa está, grande, para
// conferir num relance (camiseta A3 ou A4, caneca certa). Só mostra; não muda nada no lançamento/pedido.
// Item sem foto ou avulso mostra um aviso no lugar da foto. Mais de um item: miniaturas embaixo para trocar o foco.
// Fotos vêm do cache de ProductThumb (uma busca por produto, já reduzida pelo servidor).
public sealed class ItemPhotoPanel : Border
{
    // ProductId null = item avulso (digitado, sem cadastro). PhotoPath: caminho da foto; "" = produto sem foto; null = não sabemos.
    public sealed record Entry(Guid? ProductId,string? PhotoPath,string Name,string Detail);
    const int MaxThumbs=8;
    readonly TextBlock counter=new(){FontSize=12,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};
    readonly Border frame=new(){CornerRadius=new CornerRadius(12),ClipToBounds=true,BorderThickness=new Thickness(1),Margin=new Thickness(0,10,0,0)};
    readonly Image photo=new(){Stretch=Stretch.Uniform,Opacity=0};
    readonly StackPanel placeholder=new(){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16)};
    readonly UiIcon placeholderIcon=new(){Width=40,Height=40,HorizontalAlignment=HorizontalAlignment.Center};
    readonly TextBlock placeholderTitle=new(){FontSize=13,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,10,0,0),TextAlignment=TextAlignment.Center};
    readonly TextBlock placeholderHint=new(){FontSize=11.5,HorizontalAlignment=HorizontalAlignment.Center,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,0)};
    readonly TextBlock name=new(){FontSize=13.5,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=40,Margin=new Thickness(0,10,0,0)};
    readonly TextBlock detail=new(){FontSize=12.5,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,0)};
    readonly WrapPanel strip=new(){Margin=new Thickness(0,10,0,0)};
    IReadOnlyList<Entry> entries=[];int selected=-1;int version;string shown="",stripSignature="";
    // Clique numa miniatura: quem usa leva o foco para a linha do item (e a linha devolve o foco para cá).
    public event Action<int>? Pick;

    public ItemPhotoPanel()
    {
        SetResourceReference(BackgroundProperty,"Surface");SetResourceReference(BorderBrushProperty,"Line");
        BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(16);Padding=new Thickness(14);Margin=new Thickness(0,16,0,0);Visibility=Visibility.Collapsed;
        var header=new Grid();var eyebrow=new TextBlock{Text="ITEM EM FOCO"};eyebrow.SetResourceReference(StyleProperty,"Eyebrow");eyebrow.Margin=new Thickness(0);header.Children.Add(eyebrow);
        counter.SetResourceReference(TextBlock.ForegroundProperty,"Muted");header.Children.Add(counter);
        frame.SetResourceReference(Border.BackgroundProperty,"SurfaceAlt");frame.SetResourceReference(Border.BorderBrushProperty,"Line");
        RenderOptions.SetBitmapScalingMode(photo,BitmapScalingMode.HighQuality);
        placeholderIcon.SetResourceReference(UiIcon.ForegroundProperty,"Muted");placeholderTitle.SetResourceReference(TextBlock.ForegroundProperty,"Muted");placeholderHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        placeholder.Children.Add(placeholderIcon);placeholder.Children.Add(placeholderTitle);placeholder.Children.Add(placeholderHint);
        var inside=new Grid();inside.Children.Add(placeholder);inside.Children.Add(photo);frame.Child=inside;
        name.SetResourceReference(TextBlock.ForegroundProperty,"Ink");detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        text.Children.Add(name);text.Children.Add(detail);
        content.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});content.ColumnDefinitions.Add(new ColumnDefinition());
        content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        content.Children.Add(frame);content.Children.Add(text);Layout(false);
        // Quadrado na largura da lateral (foto inteira, sem cortar). Janela baixa: encolhe; sem altura nem para isso,
        // vira o modo compacto (foto pequena à esquerda, nome e valor ao lado) para o painel caber sem rolar.
        frame.SizeChanged+=(_,_)=>QueueFit();SizeChanged+=(_,_)=>QueueFit();
        Loaded+=(_,_)=>{scroll??=FindScroll();if(scroll!=null&&!watching){watching=true;scroll.SizeChanged+=(_,_)=>QueueFit();if(Parent is FrameworkElement owner)owner.SizeChanged+=(_,_)=>QueueFit();}QueueFit();};
        var body=new StackPanel();body.Children.Add(header);body.Children.Add(content);body.Children.Add(strip);Child=body;
    }

    readonly Grid content=new();readonly StackPanel text=new();
    ScrollViewer? scroll;bool fitQueued,watching,compact;double normalOthers;
    const double SmallestSquare=170,CompactSide=88;
    ScrollViewer? FindScroll(){DependencyObject? node=this;while(node!=null){node=VisualTreeHelper.GetParent(node);if(node is ScrollViewer sv)return sv;}return null;}
    void QueueFit(){if(fitQueued)return;fitQueued=true;Dispatcher.BeginInvoke(()=>{fitQueued=false;Fit();},System.Windows.Threading.DispatcherPriority.Loaded);}
    void Fit()
    {
        var width=content.ActualWidth;if(width<=0||Visibility!=Visibility.Visible)return;
        var space=scroll!=null&&scroll.ViewportHeight>0?scroll.ViewportHeight-(TranslatePoint(new Point(0,0),scroll).Y+scroll.VerticalOffset)-24:double.PositiveInfinity;
        if(compact)
        {
            // Volta ao quadro grande só com folga (evita alternar a cada pixel ao redimensionar).
            if(space-normalOthers>=SmallestSquare+40)Layout(false);
            return;
        }
        normalOthers=ActualHeight-frame.ActualHeight;
        var side=Math.Min(width,space-normalOthers);
        if(side<SmallestSquare){Layout(true);return;}
        if(double.IsNaN(frame.Height)||Math.Abs(frame.Height-side)>1)frame.Height=side;
    }
    void Layout(bool small)
    {
        compact=small;
        Grid.SetColumnSpan(frame,small?1:2);Grid.SetRow(text,small?0:1);Grid.SetColumn(text,small?1:0);Grid.SetColumnSpan(text,small?1:2);
        frame.Width=small?CompactSide:double.NaN;frame.Height=small?CompactSide:double.NaN;frame.Margin=new Thickness(0,10,0,0);
        text.Margin=small?new Thickness(12,10,0,0):new Thickness(0);text.VerticalAlignment=small?VerticalAlignment.Center:VerticalAlignment.Top;
        name.MaxHeight=small?57:40;name.Margin=new Thickness(0,small?0:10,0,0);
        placeholderIcon.Width=placeholderIcon.Height=small?28:40;placeholder.Margin=new Thickness(small?6:16);
        placeholderHint.Visibility=small||placeholderHint.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
        placeholderTitle.FontSize=small?11:13;
        if(!small)QueueFit();
    }
    // Chamado a cada mudança dos itens ou do foco; só troca a foto quando o produto em foco muda.
    public void Show(IReadOnlyList<Entry> items,int focus)
    {
        entries=items;selected=items.Count==0?-1:Math.Clamp(focus,0,items.Count-1);
        if(selected<0){Visibility=Visibility.Collapsed;shown="";return;}
        if(Visibility!=Visibility.Visible){Visibility=Visibility.Visible;QueueFit();}var item=entries[selected];
        counter.Text=entries.Count>1?$"{selected+1} de {entries.Count}":"";
        name.Text=item.Name;name.ToolTip=item.Name;detail.Text=item.Detail;
        var key=$"{item.ProductId}|{item.PhotoPath}";if(key!=shown){shown=key;_=LoadPhoto(item);}
        RenderStrip();
    }

    async Task LoadPhoto(Entry item)
    {
        var mine=++version;
        if(item.ProductId is not Guid id){Placeholder("file-text","Item avulso","sem cadastro de produto");return;}
        if(item.PhotoPath==""){Placeholder("image","Sem foto cadastrada","Inclua em Produtos › Editar");return;}
        // Enquanto busca: só o ícone, sem texto (evita piscar "sem foto" quando a foto chega em seguida).
        Placeholder("image","","");
        if(!ProductThumb.Ready){shown="";return;}
        BitmapSource? bitmap;try{bitmap=await ProductThumb.Large(id,item.PhotoPath);}catch{bitmap=null;}
        if(mine!=version)return;
        if(bitmap==null){Placeholder("image","Sem foto cadastrada","Inclua em Produtos › Editar");return;}
        photo.Source=bitmap;placeholder.Visibility=Visibility.Collapsed;frame.SetResourceReference(Border.BackgroundProperty,"Surface");
        photo.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(140)));
    }
    void Placeholder(string icon,string title,string hint)
    {
        photo.BeginAnimation(OpacityProperty,null);photo.Opacity=0;photo.Source=null;frame.SetResourceReference(Border.BackgroundProperty,"SurfaceAlt");
        placeholder.Visibility=Visibility.Visible;placeholderIcon.IconName=icon;
        placeholderTitle.Text=title;placeholderTitle.Visibility=title.Length>0?Visibility.Visible:Visibility.Collapsed;
        placeholderHint.Text=hint;placeholderHint.Visibility=hint.Length>0&&!compact?Visibility.Visible:Visibility.Collapsed;
    }

    void RenderStrip()
    {
        // Só redesenha quando os itens ou o foco mudam (Show é chamado a cada tecla nos valores).
        var signature=selected+"#"+string.Join("|",entries.Select(x=>$"{x.ProductId}:{x.PhotoPath}:{x.Name}"));if(signature==stripSignature)return;stripSignature=signature;
        strip.Children.Clear();strip.Visibility=entries.Count>1?Visibility.Visible:Visibility.Collapsed;if(entries.Count<2)return;
        for(var i=0;i<Math.Min(entries.Count,MaxThumbs);i++)
        {
            var index=i;var entry=entries[i];var isSelected=i==selected;
            var image=new Image{Stretch=Stretch.UniformToFill};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
            var icon=new UiIcon{IconName=entry.ProductId==null?"file-text":"image",Width=16,Height=16};icon.SetResourceReference(UiIcon.ForegroundProperty,"Muted");
            var cell=new Grid();cell.Children.Add(icon);cell.Children.Add(image);
            var tile=new Border{Width=40,Height=40,CornerRadius=new CornerRadius(8),ClipToBounds=true,BorderThickness=new Thickness(isSelected?2:1),Margin=new Thickness(0,0,6,6),Child=cell,Cursor=System.Windows.Input.Cursors.Hand,ToolTip=entry.Name};
            tile.SetResourceReference(Border.BackgroundProperty,"SurfaceAlt");
            if(isSelected)tile.SetBinding(Border.BorderBrushProperty,new Binding{Path=new PropertyPath(Ui.AccentProperty),RelativeSource=RelativeSource.Self});else tile.SetResourceReference(Border.BorderBrushProperty,"Line");
            tile.MouseLeftButtonUp+=(_,_)=>Pick?.Invoke(index);
            strip.Children.Add(tile);
            // A do item em foco usa a foto grande (já buscada); as outras, a miniatura.
            if(entry.ProductId is Guid id&&entry.PhotoPath!="")_=FillThumb(image,icon,isSelected?ProductThumb.Large(id,entry.PhotoPath):ProductThumb.Small(id,entry.PhotoPath));
        }
        if(entries.Count>MaxThumbs){var more=new TextBlock{Text=$"+{entries.Count-MaxThumbs}",FontSize=12.5,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,0,0,6)};more.SetResourceReference(TextBlock.ForegroundProperty,"Muted");strip.Children.Add(more);}
    }
    static async Task FillThumb(Image image,UiIcon icon,Task<BitmapSource?> load)
    {
        BitmapSource? bitmap;try{bitmap=await load;}catch{bitmap=null;}
        if(bitmap==null)return;image.Source=bitmap;icon.Visibility=Visibility.Collapsed;
    }
}
