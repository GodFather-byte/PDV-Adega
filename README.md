# PDV-Adega

Aplicativo desktop para ponto de venda de adegas, feito em C# com WPF.

## Requisitos

- Windows 10 ou superior.
- SDK .NET 8.

## Executar

No terminal, na pasta do projeto:

```powershell
dotnet run --project PDV-Adega.csproj
```

## Recursos

- Busca de produtos por nome, categoria ou código de barras.
- Leitor USB que funciona como teclado no campo de busca.
- Carrinho com ajuste de quantidade, desconto e identificação do cliente.
- Pagamento por Pix, cartão ou dinheiro, com cálculo e validação de troco.
- Estoque limitado à quantidade disponível e atualizado após cada venda.
- Produtos e vendas salvos como JSON em `%LOCALAPPDATA%\PDV-Adega`.

O catálogo inicial é de demonstração. Esta versão ainda não tem login, sincronização entre caixas, cadastro de produtos, integração com meios de pagamento ou emissão fiscal. Valide essas integrações antes de usar em produção.
