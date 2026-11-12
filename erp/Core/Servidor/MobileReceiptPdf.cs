using Microsoft.Data.Sqlite;
namespace GraficaCaixa;
public static class MobileReceiptPdf
{
    public static ScheduledOrder ReadSnapshot(SqliteConnection db,Guid id)
    {
        var order=WebQueries.ReadOrders(db," WHERE id=@id",("@id",id)).FirstOrDefault()
            ??throw new WebRequestException(404,"Pedido não encontrado.");
        using(var cmd=WebQueries.Command(db,"SELECT id,quantity,product FROM order_items WHERE order_id=@id ORDER BY sort_index",("@id",id)))
        using(var reader=cmd.ExecuteReader())
            while(reader.Read())order.Items.Add(new(){Id=Guid.Parse(reader.GetString(0)),Quantity=reader.GetInt32(1),Product=reader.GetString(2)});
        OperationalQueries.Details(db,order);
        WebQueries.Payments(db,order);
        return order;
    }
    public static Task<byte[]> Render(ScheduledOrder order,CancellationToken ct)
    {
        var done=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            var file=Path.Combine(Path.GetTempPath(),"cacp-receipt-"+Guid.NewGuid().ToString("N")+".pdf");
            try{ct.ThrowIfCancellationRequested();ReportWriter.WriteReceipt(order,file);ct.ThrowIfCancellationRequested();done.SetResult(File.ReadAllBytes(file));}catch(Exception ex){done.SetException(ex);}finally{if(File.Exists(file))File.Delete(file);}
        }){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();return done.Task;
    }
}
