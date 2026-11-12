using System.Globalization;
using System.Windows.Controls;
using System.Windows;
namespace GraficaCaixa.Wpf;
public static class ItemPricing
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    public static decimal Update(TextBox quantity,TextBox price,TextBox subtotal,bool subtotalDriven,out bool valid)
    {
        var quantityValid=int.TryParse(quantity.Text,out var q)&&q>0;
        if(subtotalDriven){var hasSubtotal=decimal.TryParse(subtotal.Text,NumberStyles.Currency,Pt,out var entered)&&entered>=0;price.Text=quantityValid&&hasSubtotal?(entered/q).ToString("0.00##########################",Pt):"";}
        valid=quantityValid&&decimal.TryParse(price.Text,NumberStyles.Currency,Pt,out var p)&&p>=0;
        decimal.TryParse(price.Text,NumberStyles.Currency,Pt,out var unit);
        var line=decimal.Round(q*unit,2);if(!subtotalDriven)subtotal.Text=valid?line.ToString("N2",Pt):"";return valid?line:0;
    }
}
public static class QuantityInput
{
    public static void ClearOnEntry(TextBox box)
    {
        box.GotKeyboardFocus+=(_,_)=>box.SelectAll();
    }
}
public sealed class QuantityTierBinding
{
    readonly TextBox quantity,price,subtotal;
    readonly TextBlock hint;
    readonly Func<bool> subtotalManual;
    readonly Action resetSubtotalManual;
    Product? selected;
    bool setting,manualPrice;
    public ComboBox ExactChoices { get; } = new(){Visibility=Visibility.Collapsed,MinWidth=210,HorizontalAlignment=HorizontalAlignment.Left};
    public QuantityTierBinding(TextBox quantity,TextBox price,TextBlock hint,Func<bool> manualPriceState):this(quantity,price,new TextBox(),hint,manualPriceState,()=>{}){}
    public QuantityTierBinding(TextBox quantity,TextBox price,TextBox subtotal,TextBlock hint,Func<bool> subtotalManual,Action resetSubtotalManual)
    {
        this.quantity=quantity;this.price=price;this.subtotal=subtotal;this.hint=hint;this.subtotalManual=subtotalManual;this.resetSubtotalManual=resetSubtotalManual;
        quantity.TextChanged+=(_,_)=>{if(!setting)resetSubtotalManual();Apply();};
        price.TextChanged+=(_,_)=>{if(!setting){manualPrice=true;ShowHint();}};
        subtotal.TextChanged+=(_,_)=>{if(!setting&&subtotal.IsKeyboardFocusWithin){manualPrice=true;ShowHint();}};
        ExactChoices.SelectionChanged+=(_,_)=>{if(setting||ExactChoices.SelectedItem is not ExactQuantityChoice choice)return;setting=true;try{manualPrice=false;resetSubtotalManual();quantity.Text=choice.Quantity.ToString();}finally{setting=false;}Apply();};
        hint.Visibility=Visibility.Collapsed;
    }
    public void Choose(Product product)
    {
        selected=product;manualPrice=false;
        setting=true;try{ExactChoices.ItemsSource=(QuantityPricing.HasExactTable(product)?product.ExactQuantityPrices:[]).OrderBy(x=>x.Quantity).Select(x=>new ExactQuantityChoice(x.Quantity,x.TotalPrice)).ToList();ExactChoices.DisplayMemberPath=nameof(ExactQuantityChoice.Label);}
        finally{setting=false;}
        ExactChoices.Visibility=QuantityPricing.HasExactTable(product)?Visibility.Visible:Visibility.Collapsed;
        // Só uma quantidade tabelada (ex.: 1000 adesivos): já vem escolhida, senão a linha ficaria em 1 un., sem preço.
        if(QuantityPricing.HasExactTable(product)&&product.ExactQuantityPrices.Count==1&&(!int.TryParse(quantity.Text,out var typed)||typed!=product.ExactQuantityPrices[0].Quantity))quantity.Text=product.ExactQuantityPrices[0].Quantity.ToString();
        Apply();
    }
    // Preço por tipo de cliente (Revenda/Empresa): desconto sobre o preço do produto; vale para os itens do catálogo sem preço manual.
    decimal discount;string discountLabel="";
    public void SetDiscount(decimal percent,string label){discount=Math.Max(0,percent);discountLabel=label;Apply();}
    public void Clear(){selected=null;ExactChoices.ItemsSource=null;ExactChoices.Visibility=Visibility.Collapsed;hint.Visibility=Visibility.Collapsed;}
    void ShowHint()
    {
        if(selected==null)return;
        if(manualPrice){hint.Visibility=Visibility.Visible;hint.Text="Preço manual";return;}
        if(!int.TryParse(quantity.Text,out var count)||count<=0){hint.Visibility=Visibility.Collapsed;return;}
        var entry=selected.ExactQuantityPrices.SingleOrDefault(x=>x.Quantity==count);
        var tier=QuantityPricing.Find(selected,count);
        var table=QuantityPricing.HasExactTable(selected);hint.Visibility=table||selected.UseQuantityPricing||discount>0?Visibility.Visible:Visibility.Collapsed;
        hint.Text=table?(entry==null?(count==1&&QuantityPricing.BasePrice(selected) is decimal one?$"Preço normal: {one:C2}/un.":"Quantidade sem preço tabelado"):$"Preço tabelado: {entry.TotalPrice:C2} total"):selected.UseQuantityPricing?(tier==null?"Sem faixa aplicável":$"Faixa aplicada: {tier.Label}"):"";
        if(discount>0)hint.Text=(hint.Text.Length>0?hint.Text+" · ":"")+$"Preço {discountLabel} −{discount:0.##}%";
    }
    void Apply()
    {
        if(selected==null)return;
        if(!int.TryParse(quantity.Text,out var count)||count<=0){hint.Visibility=Visibility.Collapsed;return;}
        var exact=selected.ExactQuantityPrices.Count>0;
        var entry=selected.ExactQuantityPrices.SingleOrDefault(x=>x.Quantity==count);
        setting=true;try{ExactChoices.SelectedItem=ExactChoices.Items.Cast<ExactQuantityChoice>().FirstOrDefault(x=>x.Quantity==count);}finally{setting=false;}
        ShowHint();
        if(manualPrice||subtotalManual())return;
        setting=true;
        try{var unit=QuantityPricing.UnitPrice(selected,count);if(unit is decimal value)unit=BusinessRules.ApplyDiscount(value,discount);price.Text=unit?.ToString("0.00##########################",System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))??"";}
        finally{setting=false;}
    }
}
public sealed record ExactQuantityChoice(int Quantity,decimal TotalPrice)
{
    public string Label=>string.Format(CultureInfo.GetCultureInfo("pt-BR"), "{0:N0} un. — {1:C2}", Quantity, TotalPrice);
    public override string ToString()=>Label;
}
