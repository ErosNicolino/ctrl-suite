using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;

namespace GraficaCaixa.Wpf;

// Tudo que o ERP faz com o WhatsApp Desktop passa por aqui: abre a conversa com a mensagem já escrita (e, quando há PDF,
// deixa o arquivo copiado para colar). O envio é sempre um clique da pessoa: o ERP nunca envia sozinho.
public static class WhatsAppActions
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");

    public static bool Open(Window owner,string phone,string message)
    {
        string number;
        try{number=CommunicationOperations.WhatsAppNumber(phone);}
        catch(InvalidOperationException ex){AlertWindow.Info(owner,"WhatsApp",ex.Message);return false;}
        var text=Uri.EscapeDataString(message??"");
        // WhatsApp Desktop primeiro; sem o aplicativo instalado, abre pelo navegador (wa.me).
        try{Process.Start(new ProcessStartInfo($"whatsapp://send?phone={number}"+(text.Length>0?"&text="+text:"")){UseShellExecute=true});return true;}
        catch(Win32Exception){}
        try{Process.Start(new ProcessStartInfo(CommunicationOperations.WhatsAppUrl(phone,message??"")){UseShellExecute=true});return true;}
        catch(Exception ex) when(ex is Win32Exception or InvalidOperationException){AlertWindow.Info(owner,"WhatsApp",$"Não foi possível abrir o WhatsApp.\n\n{ex.Message}");return false;}
    }

    // O link do WhatsApp não anexa arquivos: o PDF vai para a área de transferência e a conversa abre com o texto; basta colar (Ctrl+V).
    public static bool SendPdf(Window owner,string path,string phone,string message)
    {
        try{Clipboard.SetFileDropList(new StringCollection{Path.GetFullPath(path)});}
        catch(Exception ex) when(ex is System.Runtime.InteropServices.ExternalException or IOException){AlertWindow.Info(owner,"WhatsApp",$"Não foi possível copiar o PDF.\nEle está em:\n{path}\n\n{ex.Message}");return false;}
        // O aviso do ERP fica atrás do WhatsApp: explica uma vez por sessão, antes de abrir, como anexar o PDF.
        if(!pasteHintShown){pasteHintShown=true;AlertWindow.Info(owner,"Enviar PDF pelo WhatsApp","O PDF já está copiado.\n\nQuando a conversa abrir, clique na caixa de mensagem, aperte Ctrl+V para anexar o PDF e depois clique em Enviar.\n\nA mensagem de texto já vai escrita junto.");}
        if(!Open(owner,phone,message))return false;
        Toast.Show(owner,"PDF copiado. Na conversa, cole com Ctrl+V e clique em enviar.",tone:"ok");return true;
    }
    static bool pasteHintShown;

    // Variáveis dos modelos para um pedido: {cliente} {pedido} {total} {recebido} {saldo} {prazo} {pronto} {grafica} {pix} ({link} só no envio do link de acompanhamento).
    public static Dictionary<string,string> OrderValues(AppData data,ScheduledOrder order)
    {
        var balance=order.PricePending?0:order.RemainingAmount;
        return new(){
            ["cliente"]=order.CustomerName.Trim(),["pedido"]=order.Number>0?$"#{order.Number}":"",["total"]=order.OrderTotal.ToString("C2",Pt),
            ["recebido"]=order.PaidAmount.ToString("C2",Pt),["saldo"]=balance.ToString("C2",Pt),["prazo"]=order.DeadlineStarted&&order.DueDate<DateTime.MaxValue.Date?order.DueDate.ToString("dd/MM/yyyy"):"",
            ["pronto"]=ReadySince(order).ToString("dd/MM",Pt),["grafica"]=data.Company?.Name??"",
            ["pix"]=PixBrCode.MessageBlock(data.Company,balance,order.Number>0?$"PED{order.Number}":null)};
    }

    // Texto do modelo pelo nome (o salvo em Modelos de mensagem ou, se não houver, o padrão do ERP).
    public static string Message(AppData data,string templateName,IReadOnlyDictionary<string,string> values)
    {
        bool Same(CommunicationTemplate x)=>x.Name.Trim().Equals(templateName,StringComparison.OrdinalIgnoreCase);
        var template=CommunicationOperations.EffectiveTemplates(data.Prompt12??new()).FirstOrDefault(Same)??CommunicationOperations.DefaultTemplates().First(Same);
        return MessageTemplates.Render(template.Body,values);
    }

    // Dados do pedido para a mensagem (mesma consulta do editor do pedido: modelos, dados da gráfica e chave Pix).
    public static async Task<(AppData Data,ScheduledOrder Order)?> LoadOrderAsync(DataStore store,Guid id)
    {
        var data=store.IsRemote?await store.QueryRemoteAsync<AppData>($"desktop/context/order/{id}"):store.Load();
        var order=data.ScheduledOrders.FirstOrDefault(x=>x.Id==id);
        return order==null?null:(data,order);
    }

    // Pronto desde: data gravada ao entrar em "Pronto para retirada"; pedidos prontos antes desta versão usam o prazo como referência.
    public static DateTime ReadySince(ScheduledOrder order)=>order.ReadyAt??(order.DueDate<DateTime.MaxValue.Date?order.DueDate:order.CreatedAt);

    // Aviso de retirada: pedidos prontos há "PickupReminderDays" dias ou mais (Regras › Pedidos), um por vez, mais antigos primeiro.
    public static async Task PickupQueueAsync(Window owner,DataStore store)
    {
        try
        {
            var orders=new List<ScheduledOrder>();
            if(store.IsRemote)
                for(var page=1;page<=50;page++)
                {
                    var result=await store.QueryRemoteAsync<ModulePage>($"api/v1/pedidos?projection=desktop&scope=all&stage={Uri.EscapeDataString(OrderStages.Ready)}&page={page}&pageSize=100");
                    orders.AddRange(System.Text.Json.JsonSerializer.Deserialize<AppData>(result.Data)?.ScheduledOrders??[]);
                    if((long)page*result.PageSize>=result.Total)break;
                }
            else orders=store.Load().ScheduledOrders;
            var days=BusinessRules.Current.PickupReminderDays;
            var ready=orders.Where(x=>x.Stage==OrderStages.Ready&&!x.IsUncollected&&(DateTime.Today-ReadySince(x).Date).Days>=days).OrderBy(ReadySince).ToList();
            var withPhone=ready.Where(x=>CommunicationOperations.IsWhatsAppNumber(x.Contact)).ToList();
            if(withPhone.Count==0){AlertWindow.Info(owner,"Avisar retiradas",ready.Count>0?$"{ready.Count} pedido(s) pronto(s) há {days} dia(s) ou mais, mas sem WhatsApp completo no pedido.":$"Nenhum pedido pronto para retirada há {days} dia(s) ou mais.");return;}
            var items=withPhone.Select(o=>new WhatsAppQueueItem($"#{o.Number} · {o.CustomerName.Trim()}",$"Pronto há {(DateTime.Today-ReadySince(o).Date).Days} dia(s) · saldo {(o.PricePending?0:o.RemainingAmount).ToString("C2",Pt)}",o.Contact,
                async ()=>{if(await LoadOrderAsync(store,o.Id) is not var (data,order))throw new InvalidOperationException("O pedido não existe mais.");return Message(data,CommunicationOperations.PickupTemplateName,OrderValues(data,order));})).ToList();
            var skipped=ready.Count-withPhone.Count;
            new WhatsAppQueueWindow("Avisar retiradas",$"{items.Count} pedido(s) pronto(s) há {days} dia(s) ou mais"+(skipped>0?$" · {skipped} sem WhatsApp":"")+" · Esc fecha","Enviei, próximo",items){Owner=owner}.ShowDialog();
        }
        catch(Exception ex) when(ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException){AlertWindow.Info(owner,"Avisar retiradas",ex.Message);}
    }

    // Botão "Link de acompanhamento": modelo editável em Modelos de mensagem; o link também fica copiado.
    public static async Task SendTrackingAsync(Window owner,DataStore store,Guid orderId,string url)
    {
        if(await LoadOrderAsync(store,orderId) is not var (data,order))throw new InvalidOperationException("O pedido não existe mais.");
        var values=OrderValues(data,order);values["link"]=url;
        var message=Message(data,CommunicationOperations.TrackingTemplateName,values);
        if(!message.Contains(url,StringComparison.Ordinal))message+="\n\n"+url;
        Clipboard.SetText(url);if(!Open(owner,order.Contact??"",message))Toast.Show(owner,"Link copiado. Cole na conversa do cliente.",tone:"ok");
    }

    // Link "acompanhe seu pedido" (null quando desligado no servidor ou sem conexão: a mensagem segue sem ele).
    sealed record TrackingLink(string Url);
    public static async Task<string?> TrackingUrlAsync(DataStore store,Guid orderId)
    {
        if(!store.IsRemote)return null;
        try{return (await store.QueryRemoteAsync<TrackingLink>($"desktop/orders/{orderId}/acompanhamento")).Url;}
        catch(Exception){return null;}
    }

    // Depois de salvar um pedido que acabou de ficar pronto: oferece avisar o cliente (um clique para abrir, outro para enviar).
    public static async Task OfferReadyNoticeAsync(Window owner,DataStore store,string? previousStage,ScheduledOrder saved)
    {
        if(saved.Stage!=OrderStages.Ready||previousStage==OrderStages.Ready||!CommunicationOperations.IsWhatsAppNumber(saved.Contact))return;
        if(!AlertWindow.Confirm(owner,"Pedido pronto",$"Avisar {saved.CustomerName.Trim()} pelo WhatsApp que o pedido está pronto para retirada?"))return;
        try
        {
            if(await LoadOrderAsync(store,saved.Id) is not var (data,order))return;
            var message=Message(data,CommunicationOperations.ReadyTemplateName,OrderValues(data,order));
            // Com o acompanhamento ligado, a mensagem leva o link do pedido (página com etapa, saldo e Pix).
            if(await TrackingUrlAsync(store,order.Id) is string url)message+=$"\n\nAcompanhe o pedido por aqui:\n{url}";
            Open(owner,order.Contact,message);
        }
        catch(Exception ex) when(ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException){AlertWindow.Info(owner,"WhatsApp",ex.Message);}
    }
}
