using System.Windows;
using System.Windows.Controls;
using System.Globalization;
namespace GraficaCaixa.Wpf;
public sealed class FollowUpEditor : Window
{
    readonly bool debts;readonly ManualDebt? originalDebt;readonly Prospect? originalProspect;
    readonly TextBox customer=new(),contact=new(),description=new(),notes=new(){Height=100,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap},amount=new();
    readonly DatePicker origin=new();readonly DatePicker next=new();readonly ComboBox status=new();
    readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap,MaxWidth=420,FontWeight=FontWeights.SemiBold};
    void Fail(string message,Control field){error.Text=message;Ui.SetHasError(field,true);field.Focus();if(field is TextBox b){void C(object? s,TextChangedEventArgs e){b.TextChanged-=C;Ui.SetHasError(b,false);error.Text="";}b.TextChanged+=C;}}
    public ManualDebt? Debt{get;private set;}public Prospect? Prospect{get;private set;}public string CustomerText=>customer.Text.Trim();
    public FollowUpEditor(bool debts,ManualDebt? debt,Prospect? prospect)
    {
        this.debts=debts;originalDebt=debt;originalProspect=prospect;Title=debts?"Dívida avulsa":"Orçamento em acompanhamento";Width=640;Height=760;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        // UX 2.0 (pacote 3): diálogo central (cor da marca), seções numeradas e erro junto do botão. Mesmos campos e regras.
        Width=Math.Min(720,SystemParameters.WorkArea.Width-32);Height=Math.Min(760,SystemParameters.WorkArea.Height-32);ShowInTaskbar=false;
        var panel=new StackPanel();var who=new StackPanel();var deal=new StackPanel();
        void Field(Panel host,string label,FrameworkElement control){host.Children.Add(DialogKit.LabeledField(label,control).Panel);}
        Field(who,"Cliente / identificação *",customer);Field(who,"Telefone completo com DDD (opcional)",contact);Field(who,"Serviço / referência do orçamento *",description);
        panel.Children.Add(new SectionCard{Number="1",Header="Quem e o quê",Content=who});panel.Children.Add(new SectionCard{Number="2",Header=debts?"Dívida":"Acompanhamento",Content=deal});
        var notesCard=new StackPanel();panel.Children.Add(new SectionCard{Header="Observações / histórico",IsOptional=true,Content=notesCard});
        error.SetResourceReference(TextBlock.ForegroundProperty,"Danger");
        Content=new DialogFrame{Kind="Neutro",IconName=debts?"wallet":"clipboard-list",Title=Title,Subtitle=debts?"Valor devido fora de pedido · Ctrl+Enter salva · Esc fecha":"Orçamento em negociação · Ctrl+Enter salva · Esc fecha",Content=panel,Footer=DialogKit.Footer(this,"Salvar",()=>Save(this,new RoutedEventArgs())),FooterLeft=error};
        KeyboardFlow.Attach(this,()=>Save(this,new RoutedEventArgs()));Loaded+=(_,_)=>customer.Focus();
        customer.Text=debt?.CustomerName??prospect?.CustomerName??"";contact.Text=debt?.Contact??prospect?.Contact??"";description.Text=debt?.Description??prospect?.Description??"";notes.Text=debt?.Notes??prospect?.Notes??"";
        PhoneInput.Attach(contact);
        if(debts){Field(deal,"Valor total devido (R$) *",amount);amount.Text=debt?.Amount.ToString("N2")??"";Field(deal,"Data do pedido / origem da dívida *",origin);origin.SelectedDate=debt?.DebtDate??DateTime.Today;status.ItemsSource=CollectionControl.Statuses;status.SelectedItem=debt?.InitialCollectionStatus??"Não contatado";Field(deal,"Status inicial da cobrança",status);}
        else{status.ItemsSource=prospect?.OrderId!=null?new[]{"Convertido em pedido"}:new[]{"Aguardando retorno","Orçamento enviado","Cliente interessado","Não fechou"};status.SelectedItem=prospect?.Status??"Aguardando retorno";if(status.SelectedIndex<0)status.SelectedIndex=0;status.IsEnabled=prospect?.OrderId==null;Field(deal,"Valor do orçamento (R$, opcional)",amount);amount.Text=prospect?.Amount?.ToString("N2",CultureInfo.GetCultureInfo("pt-BR"))??"";Field(deal,"Situação",status);Field(deal,"Próximo retorno (opcional)",next);next.SelectedDate=prospect==null?DateTime.Today.AddDays(GraficaCaixa.BusinessRules.Current.FollowUpDays):prospect.NextContactAt;deal.Children.Add(DialogKit.Hint(prospect?.LastContactAt is DateTime at?$"Último contato: {at:dd/MM/yyyy HH:mm}":"Nenhum contato registrado",0));}
        notes.VerticalContentAlignment=VerticalAlignment.Top;notes.Padding=new Thickness(0,8,0,8);notesCard.Children.Add(notes);UnsavedChanges.Attach(this,()=>Save(this,new RoutedEventArgs()));
    }
    void Save(object s,RoutedEventArgs e)
    {
        if(CustomerText.Length==0){Fail("Informe o cliente ou uma identificação.",customer);return;}if(string.IsNullOrWhiteSpace(description.Text)){Fail("Informe o serviço ou a referência.",description);return;}
        if(!PhoneText.IsCompleteOrEmpty(contact.Text)&&PhoneText.Digits(contact.Text)!=PhoneText.Digits(originalDebt?.Contact??originalProspect?.Contact)){Fail(PhoneText.IncompleteMessage,contact);return;}
        if(debts){if(!decimal.TryParse(amount.Text,NumberStyles.Currency,CultureInfo.GetCultureInfo("pt-BR"),out var value)||value<=0){Fail("Informe um valor maior que zero.",amount);return;}if(origin.SelectedDate is not DateTime originDate||originDate.Date>DateTime.Today){Fail("Informe uma data de origem válida, sem data futura.",origin);return;}Debt=new ManualDebt{CustomerId=originalDebt?.CustomerName==CustomerText&&PhoneText.Normalize(originalDebt.Contact)==PhoneText.Normalize(contact.Text)?originalDebt.CustomerId:null,Items=originalDebt?.Items??[],OriginDate=originDate.Date,InitialCollectionStatus=status.SelectedItem?.ToString()??"Não contatado",Id=originalDebt?.Id??Guid.NewGuid(),CreatedAt=originalDebt?.CreatedAt??DateTime.Now,CustomerName=CustomerText,Contact=PhoneText.Normalize(contact.Text),Description=description.Text.Trim(),Notes=notes.Text.Trim(),Amount=value,SaleIds=originalDebt?.SaleIds.ToList()??[]};}
        else{decimal? value=null;if(!string.IsNullOrWhiteSpace(amount.Text)){if(!decimal.TryParse(amount.Text,NumberStyles.Currency,CultureInfo.GetCultureInfo("pt-BR"),out var parsed)||parsed<0){Fail("Informe o valor do orçamento (ou deixe em branco).",amount);return;}value=decimal.Round(parsed,2);}
            Prospect=new Prospect{Amount=value,Id=originalProspect?.Id??Guid.NewGuid(),CreatedAt=originalProspect?.CreatedAt??DateTime.Now,CustomerName=CustomerText,Contact=PhoneText.Normalize(contact.Text),Description=description.Text.Trim(),Notes=notes.Text.Trim(),Status=status.SelectedItem?.ToString()??"Aguardando retorno",NextContactAt=next.SelectedDate,LastContactAt=originalProspect?.LastContactAt,OrderId=originalProspect?.OrderId};if(Prospect.Status is "Não fechou" or "Convertido em pedido")Prospect.NextContactAt=null;}
        DialogResult=true;
    }
}
