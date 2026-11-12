namespace GraficaCaixa.Wpf;
// Pagamento que já está no caixa aberto como lançamento (venda feita antes do pedido): pergunta se é o mesmo pagamento,
// para ligar ao pedido em vez de lançar de novo e contar duas vezes no faturamento (pedidos #167 e #237).
internal static class CashSaleLink
{
    // Partes do lançamento a ligar; vazio quando não há lançamento livre do cliente ou a pessoa disse que é outro pagamento.
    // exact: só oferece o lançamento com esse valor (pagamento digitado na tela do pedido).
    public static async Task<List<Guid>> AskAsync(System.Windows.Window owner,DataStore store,string name,string contact,decimal balance,decimal? exact=null)
    {
        AppData data;try{data=await store.BusinessContextAsync("cash");}catch{return [];} // sem acesso ao caixa: segue como antes
        var groups=ReceivableViews.FreeCashSales(data,name,contact).Select(x=>ReceivableViews.FreeGroup(data,x)).DistinctBy(g=>g[0].Id)
            .Select(g=>(Parts:g,Total:g.Sum(x=>ReceivableViews.EffectiveTotal(data,x)))).Where(x=>x.Total<=balance&&(exact==null||x.Total==exact)).ToList();
        if(groups.Count==0)return [];
        var (parts,total)=groups[0];
        var text=$"{total:C2} em {string.Join(" + ",parts.Select(x=>x.PaymentType).Distinct())}, às {parts[0].CreatedAt:HH:mm}\n{parts[0].Product}";
        return AlertWindow.Confirm(owner,"Este pagamento já está no caixa?",$"Este cliente tem um lançamento no caixa aberto que não está em nenhum pedido:\n\n{text}\n\nÉ o pagamento deste pedido? Se sim, ele é ligado ao pedido sem lançar de novo (não conta duas vezes no faturamento).",yes:"Sim, usar este lançamento",no:"Não, é outro pagamento")
            ?parts.Select(x=>x.Id).ToList():[];
    }
}
