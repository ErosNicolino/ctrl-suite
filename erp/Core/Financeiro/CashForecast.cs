namespace GraficaCaixa;

// Fluxo de caixa previsto (Área do proprietário › Fluxo previsto): o que deve entrar e sair nas próximas semanas.
// Entra: saldo dos pedidos ainda não entregues, pela data de entrega (atrasados contam na semana atual).
// Sai: despesas pendentes pelo vencimento (vencidas contam na semana atual) e as próximas parcelas das despesas recorrentes ainda não lançadas.
// Fica de fora (sem data para prever): saldo de pedidos entregues e dívidas avulsas (Devedores), pedidos sem prazo iniciado e compras a fazer (sem valor).
public sealed record ForecastItem(DateTime Date,string Kind,string Title,decimal Amount,bool In,bool Overdue,bool Projected);
public sealed record ForecastWeek(DateTime Start,DateTime End,decimal In,decimal Out){public decimal Net=>In-Out;}
public sealed record CashForecast(List<ForecastWeek> Weeks,List<ForecastItem> Items,decimal UndatedReceivable,int UndatedCount)
{
    public decimal TotalIn=>Weeks.Sum(x=>x.In);
    public decimal TotalOut=>Weeks.Sum(x=>x.Out);

    public static CashForecast Build(AppData data,DateTime today,int weeks=5)
    {
        today=today.Date;var start=today.AddDays(-(((int)today.DayOfWeek+6)%7));var end=start.AddDays(7*weeks-1);var items=new List<ForecastItem>();
        DateTime Clamp(DateTime date)=>date.Date<start?start:date.Date;
        foreach(var order in data.ScheduledOrders.Where(x=>!x.PricePending&&x.RemainingAmount>0&&!x.IsUncollected&&x.CancelledAt==null&&LossOperations.IsNormalReceivable(data,x.Id)))
        {
            if(order.Stage==OrderStages.Delivered||!order.DeadlineStarted)continue;
            var due=order.DueDate.Date;if(due>end)continue;
            items.Add(new(Clamp(due<today?today:due),"Saldo do pedido",$"#{order.Number} · {order.CustomerName}",order.RemainingAmount,true,due<today,false));
        }
        var state=data.Prompt12??new();
        foreach(var expense in (state.Expenses??[]).Where(x=>x.Active&&x.Status==ExpenseStatus.Pending&&x.DueDate.Date<=end))
            items.Add(new(Clamp(expense.DueDate<today?today:expense.DueDate),"Despesa",expense.Description,expense.Amount,false,expense.DueDate.Date<today,false));
        foreach(var expense in UpcomingRecurring(state,end).Where(x=>x.DueDate.Date>=start))
            items.Add(new(Clamp(expense.DueDate),"Recorrente (prevista)",expense.Description,expense.Amount,false,false,true));
        var undated=data.ScheduledOrders.Where(x=>!x.PricePending&&x.RemainingAmount>0&&!x.IsUncollected&&x.CancelledAt==null&&LossOperations.IsNormalReceivable(data,x.Id)&&(x.Stage==OrderStages.Delivered||!x.DeadlineStarted)).ToList();
        var debts=(data.ManualDebts??[]).Select(x=>x.Remaining(data)).Where(x=>x>0).ToList();
        var list=Enumerable.Range(0,weeks).Select(i=>{var s=start.AddDays(7*i);var e=s.AddDays(6);var inWeek=items.Where(x=>x.Date>=s&&x.Date<=e).ToList();return new ForecastWeek(s,e,inWeek.Where(x=>x.In).Sum(x=>x.Amount),inWeek.Where(x=>!x.In).Sum(x=>x.Amount));}).ToList();
        return new(list,items.OrderBy(x=>x.Date).ThenBy(x=>x.In).ThenByDescending(x=>x.Amount).ToList(),undated.Sum(x=>x.RemainingAmount)+debts.Sum(),undated.Count+debts.Count);
    }

    // Parcelas futuras das séries recorrentes que ainda não foram lançadas, até o fim da previsão (mesma regra de "Lançar recorrentes").
    static IEnumerable<Expense> UpcomingRecurring(Prompt12State state,DateTime end)
    {
        var expenses=state.Expenses??[];var month=new DateTime(end.Year,end.Month,1);
        string Key(Expense x)=>x.RecurrenceKey.Length>0?x.RecurrenceKey:x.Description.Trim().ToLowerInvariant();
        foreach(var series in expenses.Where(x=>x.Active&&x.Recurring).GroupBy(Key))
        {
            var last=series.OrderByDescending(x=>x.Competence).First();var every=last.EveryMonths;if(every<=0)continue;
            for(var next=last.Competence.AddMonths(every);next<=month;next=next.AddMonths(every))
            {
                if(expenses.Any(x=>x.Recurring&&Key(x)==series.Key&&x.Competence==next))continue;
                var shift=(next.Year-last.Competence.Year)*12+next.Month-last.Competence.Month;var due=last.DueDate.AddMonths(shift);
                if(due.Date<=end)yield return new Expense{Description=last.Description,Amount=last.Amount,Competence=next,DueDate=due,Status=ExpenseStatus.Pending,Recurring=true};
            }
        }
    }
}
