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
- Controle de turno com abertura, fundo de troco, suprimentos, sangrias e fechamento conferido.
- Relatórios por período de vendas, pagamentos, produtos, sessões e movimentações de caixa.
- Detalhamento de itens vendidos, desempenho por categoria e posição de estoque a preço de venda.
- Exportação CSV do relatório selecionado, compatível com planilhas.

O catálogo inicial é de demonstração. Como ainda não há custo de compra, os relatórios não calculam margem/lucro. Esta versão também não tem login, sincronização entre caixas, cadastro de produtos, integração com meios de pagamento ou emissão fiscal. Valide essas integrações antes de usar em produção.
