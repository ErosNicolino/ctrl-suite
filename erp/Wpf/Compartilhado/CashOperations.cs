namespace GraficaCaixa;
public static class CashOperations
{
    public static void Withdraw(AppData data,Guid sessionId,CashWithdrawal withdrawal,OperationContext? context=null)
    {
        var session=data.Sessions.FirstOrDefault(x=>x.Id==sessionId)??throw new InvalidOperationException("Caixa não encontrado.");
        if(!session.IsOpen)throw new InvalidOperationException("O caixa foi fechado. Atualize a tela.");
        if(session.Withdrawals.Any(x=>x.Id==withdrawal.Id))return;
        if(withdrawal.Amount<=0||decimal.Round(withdrawal.Amount,2)!=withdrawal.Amount)throw new InvalidOperationException("Informe uma sangria maior que zero, com até duas casas decimais.");
        if(string.IsNullOrWhiteSpace(withdrawal.Reason))throw new InvalidOperationException("Informe o motivo da retirada.");
        if(withdrawal.CreatedAt<session.OpenedAt||withdrawal.CreatedAt>DateTime.Now.AddMinutes(1))throw new InvalidOperationException("A data deve estar entre a abertura do caixa e o momento atual.");
        if(withdrawal.Amount>session.ExpectedCash)throw new InvalidOperationException("A sangria não pode superar o dinheiro esperado no caixa.");
        withdrawal.UserName=context?.UserName??"";session.Withdrawals.Add(withdrawal);
        AuditService.Record(data,context??OperationContext.Administrator("Sistema"),"Sangria","Sangria",withdrawal.Id.ToString(),"",withdrawal.Amount.ToString("0.00"),withdrawal.Reason,$"Caixa {session.OpenedAt:dd/MM/yyyy HH:mm}");
    }
    // Corrige só o dinheiro esperado do caixa aberto; lançamentos, recebimentos e lucro não mudam. Para desfazer, registra-se outro ajuste no sentido contrário.
    public static void Adjust(AppData data,Guid sessionId,CashAdjustment adjustment,OperationContext? context=null)
    {
        var session=data.Sessions.FirstOrDefault(x=>x.Id==sessionId)??throw new InvalidOperationException("Caixa não encontrado.");
        if(!session.IsOpen)throw new InvalidOperationException("O caixa foi fechado. Atualize a tela.");
        if(session.Adjustments.Any(x=>x.Id==adjustment.Id))return;
        if(adjustment.Amount==0||decimal.Round(adjustment.Amount,2)!=adjustment.Amount)throw new InvalidOperationException("Informe um ajuste diferente de zero, com até duas casas decimais.");
        adjustment.Reason=(adjustment.Reason??"").Trim();
        if(adjustment.Reason.Length<5||adjustment.Reason.Length>1000)throw new InvalidOperationException("Informe o motivo do ajuste, com 5 a 1000 caracteres.");
        if(adjustment.Amount<0&&session.ExpectedCash+adjustment.Amount<0)throw new InvalidOperationException("O ajuste não pode deixar o dinheiro esperado negativo.");
        adjustment.CreatedAt=DateTime.Now;adjustment.UserName=context?.UserName??"";
        var before=session.ExpectedCash;session.Adjustments.Add(adjustment);
        AuditService.Record(data,context??OperationContext.Administrator("Sistema"),"Ajuste de caixa","Ajuste de caixa",adjustment.Id.ToString(),"",System.Text.Json.JsonSerializer.Serialize(new{adjustment.Amount,ExpectedBefore=before,ExpectedAfter=session.ExpectedCash}),adjustment.Reason,$"Caixa {session.OpenedAt:dd/MM/yyyy HH:mm} · {session.Id}");
    }
}
