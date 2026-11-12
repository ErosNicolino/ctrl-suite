using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace GraficaCaixa.Wpf;

// Ficha do pedido em PDF: um único caminho de geração (salva em Impressões e marca "PDF gerado"),
// usado por "Gerar e imprimir ficha" (Pedidos) e pelo botão PDF do Editar pedido.
public static class OrderSheetPdf
{
    public static async Task<string> GenerateAsync(DataStore store,IReadOnlyList<ScheduledOrder> orders,CancellationToken cancellation=default)
    {
        if(store.IsRemote)
        {
            var result=await store.RemoteBusinessAsync("order.pdf",Guid.Empty,null,new OrderPdfArgs(orders.Select(x=>new OrderPdfItem(x.Id,x.Revision)).ToList()),cancellation);
            return DocumentPaths.ResolveServerDocument(store,result.Value!.Value.GetString()!);
        }
        var byId=store.Load().ScheduledOrders.ToDictionary(x=>x.Id);var current=orders.Where(x=>byId.ContainsKey(x.Id)).Select(x=>byId[x.Id]).ToList();
        var path=ReportWriter.BuildAvailableOrderSheetPath(DocumentPaths.Folder(store,"Impressões"),current);ReportWriter.WriteOrderSheet(current,path);
        store.Update(d=>{OrderOperations.MarkPdfGenerated(d,current.Select(x=>x.Id).ToList(),DateTime.Now);return 0;});
        return path;
    }

    // Botão PDF do pedido: gera a ficha e oferece Abrir, Salvar como… e Enviar pelo WhatsApp.
    public static async void Offer(Window owner,FrameworkElement anchor,DataStore store,ScheduledOrder order)
    {
        string path;
        anchor.IsEnabled=false;
        try{path=await GenerateAsync(store,[order]);}
        catch(Exception ex){AlertWindow.Info(owner,"Não foi possível gerar o PDF",ex.Message);return;}
        finally{anchor.IsEnabled=true;}
        ShowActions(owner,anchor,path,order.Contact??"",()=>"");
    }

    // Menu de um PDF já gerado (ficha, recibo do pedido): Abrir, Salvar como… e Enviar pelo WhatsApp com a mensagem pronta.
    public static void ShowActions(Window owner,FrameworkElement anchor,string path,string contact,Func<string> message)
    {
        var menu=new ContextMenu{PlacementTarget=anchor,Placement=PlacementMode.Bottom};
        MenuItem Item(string text,string icon,Action action){var item=new MenuItem{Header=text,Icon=new UiIcon{IconName=icon,Width=16,Height=16}};item.Click+=(_,_)=>action();menu.Items.Add(item);return item;}
        Item("Abrir","file-text",()=>PdfDocuments.Open(owner,path));
        Item("Salvar como…","download",()=>SaveAs(owner,path));
        var whatsapp=Item("Enviar pelo WhatsApp","message-circle",()=>WhatsAppActions.SendPdf(owner,path,contact,message()));
        if(!CommunicationOperations.IsWhatsAppNumber(contact)){whatsapp.IsEnabled=false;whatsapp.ToolTip="Pedido sem telefone completo com DDD.";ToolTipService.SetShowOnDisabled(whatsapp,true);}
        menu.IsOpen=true;
    }

    static void SaveAs(Window owner,string path)
    {
        var dialog=new Microsoft.Win32.SaveFileDialog{FileName=Path.GetFileName(path),Filter="PDF (*.pdf)|*.pdf",DefaultExt=".pdf"};
        if(dialog.ShowDialog(owner)!=true)return;
        try{File.Copy(path,dialog.FileName,true);Toast.Show(owner,"PDF salvo.",tone:"ok");}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){AlertWindow.Info(owner,"Não foi possível salvar",ex.Message);}
    }
}
