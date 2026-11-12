# Recuperação do ERP após formatação do PC1

Este procedimento pressupõe que `D:\Grafica CA CP\ERP` foi preservado integralmente. O script não procura bancos em outras unidades e não cria banco, identidade ou estrutura vazia.

## Antes de formatar

1. Feche o ERP e confirme que os clientes estão desconectados.
2. Execute `Validar_ERP_Antes_Formatacao.ps1` para gerar o diagnóstico.
3. Confirme que existe um backup recente fora do disco que será formatado.
4. Preserve integralmente o SSD que contém `D:\Grafica CA CP\ERP`.

## Depois de reinstalar o Windows

1. **Manual:** instale o Windows e os drivers do PC1.
2. **Manual:** conecte o SSD preservado e atribua a letra `D:`. Não crie uma estrutura vazia se a pasta não aparecer.
3. **Manual:** instale e conecte o Tailscale à conta/rede correta. A LAN funciona sem Tailscale; o acesso remoto depende dele.
4. **Manual:** confira se `D:\Grafica CA CP\ERP` contém `Programa`, `Dados`, `Configuracao` e `Servidor\Identidade`.
5. **Automático:** abra PowerShell como Administrador e execute:

   ```powershell
   D:\Grafica CA CP\ERP\Instalacao\Restaurar_ERP_Pos_Formatacao.ps1
   ```

6. **Automático:** o script valida a base existente, `servidor.json`, identidade, ACLs, executável self-contained, serviço, Recovery, porta 5074 e `/ping`.
7. **Diagnóstico:** o script verifica LAN, Tailscale e o compartilhamento SMB. Ele não instala Tailscale e não cria/afrouxa o compartilhamento automaticamente.
8. **Manual:** se o compartilhamento `ERP` tiver sido perdido, recrie-o para a pasta física `D:\Grafica CA CP\ERP` com acesso somente às contas necessárias. Preserve separadamente o compartilhamento antigo `Grafica CA CP` apontado para G:. Não conceda `Everyone: Full Control` nem `Users: Full Control`.
9. **Manual:** abra o ERP no PC1 e valide dados, documentos, fotos, PDFs e relatórios.
10. **Manual:** valide o PC2 pela LAN e o notebook pelo Tailscale.

Quando a identidade persistente é preservada, a Bearer Key, o certificado e o CertificatePin continuam iguais. PC2 e notebook não devem precisar de novo PairingCode apenas por causa da formatação. Mudanças de nome/IP podem exigir que o endpoint seja atualizado em uma fase futura, mas a identidade criptográfica permanece a mesma.

