namespace GraficaCaixa.Wpf;
// Repetir pedido: abre o rascunho na tela normal do pedido. Devolve a tela só se a pessoa confirmou; quem chama é que grava.
internal static class OrderRepeatUi
{
    public static async Task<ScheduledOrderDialog?> OpenAsync(System.Windows.Window owner,DataStore store,Guid orderId)
    {
        var data=store.IsRemote?await store.QueryRemoteAsync<AppData>($"desktop/context/order/{orderId}"):store.Load();
        var source=data.ScheduledOrders.FirstOrDefault(x=>x.Id==orderId)??throw new InvalidOperationException("Pedido não encontrado. Atualize a tela.");
        var dialog=new ScheduledOrderDialog(OrderRepeat.Draft(source),store){Owner=owner};
        ((System.Windows.Controls.TextBlock)dialog.FindName("TitleText")).Text=$"Repetir pedido #{source.Number}";((System.Windows.Controls.Button)dialog.FindName("SaveButton")).Content="Salvar pedido";
        return dialog.ShowDialog()==true?dialog:null;
    }
}
