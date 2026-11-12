using System.Collections.ObjectModel;
using System.Globalization;
namespace GraficaCaixa.Wpf;

public partial class BudgetSettingsWindow : System.Windows.Window
{
    bool writeBusy;
    List<BudgetRule> baselineRules=[];readonly GraficaCaixa.DataStore store;readonly ObservableCollection<BudgetRuleRow> rows=[];
    public BudgetSettingsWindow(GraficaCaixa.DataStore store,AppData? prepared=null){InitializeComponent();UnsavedChanges.Attach(this,()=>Save_Click(this,new System.Windows.RoutedEventArgs()),()=>{RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell,true);RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row,true);return System.Text.Json.JsonSerializer.Serialize(rows);});this.store=store;baselineRules=(prepared??(store.IsRemote?throw new InvalidOperationException("Carregue as regras antes de abrir."):store.Load())).BudgetRules;foreach(var x in baselineRules)rows.Add(new(x));RulesGrid.ItemsSource=rows;}
    void Add_Click(object s,System.Windows.RoutedEventArgs e){var row=new BudgetRuleRow(new GraficaCaixa.BudgetRule{Product="Novo produto",MarkupMultiplier=2});rows.Add(row);RulesGrid.SelectedItem=row;RulesGrid.ScrollIntoView(row);}
    void Remove_Click(object s,System.Windows.RoutedEventArgs e){if(RulesGrid.SelectedItem is not BudgetRuleRow row){AlertWindow.Info(this,"Selecione um produto","Escolha a linha que deseja remover.");return;}if(rows.Count==1){AlertWindow.Info(this,"Mantenha um produto","O orçamento precisa ter pelo menos uma regra.");return;}rows.Remove(row);}
    static bool Parse(string text,string field,out decimal value,out string error){if((decimal.TryParse(text,NumberStyles.Number,CultureInfo.CurrentCulture,out value)||decimal.TryParse(text.Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out value))&&value>=0){error="";return true;}error=$"{field} deve ser um número maior ou igual a zero.";return false;}
    async void Save_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(writeBusy)return;writeBusy=true;try{
        RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell,true);RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row,true);var result=new List<GraficaCaixa.BudgetRule>();
        foreach(var row in rows){if(string.IsNullOrWhiteSpace(row.Product)){AlertWindow.Info(this,"Produto sem nome","Preencha o nome de todos os produtos.");return;}if(!Parse(row.SquareMeterRate,"R$/m²",out var rate,out var error)||!Parse(row.SizeFixedCost,"Adicional do tamanho",out var fixedCost,out error)||!Parse(row.MinimumCost,"Custo mínimo",out var minimum,out error)||!Parse(row.MarkupMultiplier,"Multiplicador",out var multiplier,out error)||!Parse(row.NoArtworkFee,"Frete de retirada",out var noArt,out error)||!Parse(row.ArtworkFee,"Adicional obrigatório de arte",out var art,out error)){AlertWindow.Info(this,"Valor inválido",$"{row.Product}: {error}");return;}if(multiplier==0){AlertWindow.Info(this,"Multiplicador inválido",$"{row.Product}: o multiplicador deve ser maior que zero.");return;}result.Add(new GraficaCaixa.BudgetRule{Id=row.Id,Product=row.Product.Trim(),SquareMeterRate=rate,SizeFixedCost=fixedCost,MinimumCost=minimum,MarkupMultiplier=multiplier,NoArtworkFee=noArt,ArtworkFee=art});}
        try{await BusinessUi.ExecuteAsync(store,"budget.save",Guid.Empty,baselineRules,new BudgetArgs(result),()=>store.Update(data=>{data.BudgetRules=result;data.AuditTrail.Add(new GraficaCaixa.AuditEntry{Action="Orçamento configurado",Description=$"{result.Count} regra(s) de preço atualizadas"});return 0;}));DialogResult=true;}catch(Exception ex){AlertWindow.Info(this,"Não foi possível salvar",ex.Message);}
    
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
    void Cancel_Click(object s,System.Windows.RoutedEventArgs e)=>Close();
}

public sealed class BudgetRuleRow
{
    public Guid Id{get;}public string Product{get;set;}public string SquareMeterRate{get;set;}public string SizeFixedCost{get;set;}public string MinimumCost{get;set;}public string MarkupMultiplier{get;set;}public string NoArtworkFee{get;set;}public string ArtworkFee{get;set;}
    public BudgetRuleRow(GraficaCaixa.BudgetRule x){Id=x.Id;Product=x.Product;SquareMeterRate=F(x.SquareMeterRate);SizeFixedCost=F(x.SizeFixedCost);MinimumCost=F(x.MinimumCost);MarkupMultiplier=F(x.MarkupMultiplier);NoArtworkFee=F(x.NoArtworkFee);ArtworkFee=F(x.ArtworkFee);}static string F(decimal value)=>value.ToString("0.##",CultureInfo.CurrentCulture);
}
