using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

// Uma mensagem por vez para uma lista de clientes (cobrança dos Devedores, aviso de retirada dos Pedidos).
// O ERP abre a conversa com o texto pronto; a pessoa envia no WhatsApp e volta para "Enviei, próximo". Nada é enviado sozinho.
public sealed record WhatsAppQueueItem(string Title,string Detail,string Phone,Func<Task<string>> Message,Func<Task>? OnSent=null);

public sealed class WhatsAppQueueWindow : Window
{
    readonly IReadOnlyList<WhatsAppQueueItem> items;
    readonly TextBlock progress=new(),title=new(){TextWrapping=TextWrapping.Wrap},detail=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,12)};
    readonly TextBox message=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=200,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(0,6,0,6)};
    readonly Button open=new(){Content="Abrir no WhatsApp"},sent=new(){Content="Enviei, próximo",Margin=new Thickness(8,0,0,0)},skip=new(){Content="Pular",Margin=new Thickness(8,0,0,0)};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    int index=-1,sentCount,skipped;bool busy;

    public WhatsAppQueueWindow(string heading,string subtitle,string sentText,IReadOnlyList<WhatsAppQueueItem> items)
    {
        this.items=items;Title=heading;Width=Math.Min(620,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
        sent.Content=sentText;
        open.Style=(Style)FindResource("PrimaryButton");sent.Style=(Style)FindResource("SoftButton");skip.Style=(Style)FindResource("GhostButton");
        title.Style=(Style)FindResource("SectionTitle");detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");progress.Style=(Style)FindResource("Eyebrow");status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var panel=new StackPanel{Children={progress,title,detail,DialogKit.LabeledField("Mensagem (pode ajustar antes de abrir)",message).Panel,new WrapPanel{Children={open,sent,skip},Margin=new Thickness(0,4,0,0)},status}};
        status.Margin=new Thickness(0,10,0,0);
        open.Click+=(_,_)=>Open();sent.Click+=async(_,_)=>await SentAsync();skip.Click+=async(_,_)=>{skipped++;await NextAsync();};
        var close=new Button{Content="Fechar",Style=(Style)FindResource("GhostButton")};close.Click+=(_,_)=>Close();
        Content=new DialogFrame{Kind="Neutro",IconName="message-circle",Title=heading,Subtitle=subtitle,Content=panel,Footer=new StackPanel{Orientation=Orientation.Horizontal,Children={close}},ScrollBody=false};
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;Close();}};
        Loaded+=async(_,_)=>await NextAsync();
    }

    void Open()
    {
        if(index<0||index>=items.Count||message.Text.Trim().Length==0)return;
        if(WhatsAppActions.Open(this,items[index].Phone,message.Text)){sent.IsEnabled=true;status.Text="Conversa aberta. Envie no WhatsApp e volte aqui para seguir.";}
    }

    async Task SentAsync()
    {
        if(busy||index<0||index>=items.Count)return;busy=true;
        try{if(items[index].OnSent is {} done)await done();sentCount++;}
        catch(Exception ex) when(ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException){busy=false;AlertWindow.Info(this,"Não foi possível registrar",ex.Message);return;}
        busy=false;await NextAsync();
    }

    async Task NextAsync()
    {
        index++;
        if(index>=items.Count){Finish();return;}
        var item=items[index];
        progress.Text=$"{index+1} DE {items.Count}";title.Text=item.Title;detail.Text=item.Detail;
        open.IsEnabled=sent.IsEnabled=skip.IsEnabled=false;message.Text="Preparando a mensagem…";status.Text="";
        try{message.Text=await item.Message();open.IsEnabled=true;}
        catch(Exception ex) when(ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException){message.Text="";status.Text="Não foi possível preparar esta mensagem: "+ex.Message;}
        skip.IsEnabled=true;open.Focus();
    }

    void Finish()
    {
        progress.Text="CONCLUÍDO";title.Text=$"{sentCount} enviada(s) · {skipped} pulada(s)";detail.Text="Pode fechar esta janela.";
        message.Text="";message.IsEnabled=open.IsEnabled=sent.IsEnabled=skip.IsEnabled=false;status.Text="";
    }
}
