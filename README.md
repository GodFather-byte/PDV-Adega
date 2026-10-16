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

## Build automático do executável

O workflow `.github/workflows/build-windows.yml` publica uma versão self-contained para Windows x64 em cada push e pull request, e também pode ser iniciado manualmente pela aba **Actions** do GitHub. Ao concluir, baixe o ZIP `PDV-Adega-win-x64` na seção **Artifacts** da execução. O executável inclui o runtime do .NET e não exige instalá-lo separadamente; é necessário extrair o ZIP antes de abrir o aplicativo.

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
- Licenciamento por assinatura com ativação vinculada ao computador e até 7 dias de operação offline.
- Painel web para criar, renovar e revogar licenças, ajustar o limite de computadores e liberar ativações.

O catálogo inicial é de demonstração. Como ainda não há custo de compra, os relatórios não calculam margem/lucro. O PDV ainda não tem contas individuais de operadores, sincronização entre caixas, cadastro de produtos, integração com meios de pagamento ou emissão fiscal. Valide essas integrações antes de usar em produção.

## Servidor e painel de licenças

O servidor ASP.NET Core fica em `LicensingServer` e entrega o painel web e a API. Publique-o em um servidor com HTTPS e armazenamento persistente. A conta administrativa é única; sua senha não é armazenada no código nem no banco.

No PowerShell, defina uma senha administrativa com pelo menos 16 caracteres e inicie o serviço:

```powershell
$env:PDV_LICENSE_ADMIN_PASSWORD = "use-uma-senha-forte-com-mais-de-16-caracteres"
dotnet run --project LicensingServer\LicensingServer.csproj
```

Abra a URL HTTPS exibida pelo ASP.NET Core. O painel fica na raiz (`/`) e a documentação da API em `/swagger`. Configure `PDV_LICENSE_DATA_PATH` para o caminho persistente do arquivo `licenses.json` e `PDV_LICENSE_KEY_RING_PATH` para um diretório persistente das chaves de autenticação. Em hospedagem na nuvem, monte ambos em armazenamento persistente, mantenha apenas uma instância do serviço e faça backups regulares. Se estiver atrás de um proxy TLS, configure corretamente os cabeçalhos encaminhados e confie somente no proxy da hospedagem.

No painel, crie uma licença informando cliente, vencimento e quantidade de computadores. Copie a chave exibida uma única vez e informe ao cliente. Na primeira abertura do PDV, informe a URL HTTPS do servidor e a chave. O mesmo computador será reconhecido nas validações seguintes; dispositivos vinculados e última conexão aparecem no painel.

O PDV valida a assinatura online ao iniciar. Se o servidor ficar indisponível, uma licença previamente validada ainda funciona por até 7 dias desde a última validação e nunca além da data de expiração armazenada. Chaves e estado local são protegidos com DPAPI para o usuário do Windows atual. Revogações e renovações chegam ao PDV quando ele voltar a se conectar.

O arquivo de licenças e as chaves de proteção são dados operacionais críticos: configure volume persistente, HTTPS, senha forte e backup antes de usar em produção. A API requer .NET 8 SDK/runtime para execução e build.
