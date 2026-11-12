using System;
using System.Collections.Generic;

namespace GraficaCaixa;

public static class MessageTemplates
{
	public static string Render(string template, IReadOnlyDictionary<string, string> values)
	{
		string text = template;
		string[] array = new string[11] { "cliente", "pedido", "total", "recebido", "saldo", "prazo", "referencia", "grafica", "pix", "pronto", "link" };
		foreach (string text2 in array)
		{
			text = text.Replace("{" + text2 + "}", values.GetValueOrDefault(text2) ?? "", StringComparison.OrdinalIgnoreCase);
		}
		// Variável vazia não deixa "Olá, , tudo bem?" nem "**" na mensagem.
		text = text.Replace(", ,", ",").Replace("**", "");
		return text;
	}
}
