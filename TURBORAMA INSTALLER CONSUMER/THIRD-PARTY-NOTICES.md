# Componentes de terceiros do TurboRama

Os componentes incorporados pertencem aos respectivos autores e fornecedores. A licenca do TurboRama nao substitui, amplia nem restringe as licencas desses componentes. Os instaladores originais, suas assinaturas, termos, avisos de copyright e arquivos de licenca devem permanecer inalterados. Obter um arquivo de uma fonte oficial nao elimina as condicoes de redistribuicao aplicaveis.

## Eclipse Temurin / OpenJDK

Os quatro runtimes opcionais x64 sao Eclipse Temurin JRE 8u504-b01, 17.0.20.1+1, 21.0.12.1+1 e 25.0.4.1+1. Eclipse Temurin e fornecido sob a GNU General Public License, versao 2, com Classpath Exception, junto de avisos e licencas adicionais dos componentes incluidos. Preserve os avisos e os termos completos fornecidos no pacote; este documento e apenas um indice, nao uma licenca substituta nem uma promessa de suporte pela Eclipse Foundation.

- Java 8: os termos acompanham a instalacao nos arquivos `LICENSE`, `NOTICE` e `ASSEMBLY_EXCEPTION` na pasta do runtime.
- Java 17, 21 e 25: o runtime contem `NOTICE` e a pasta `legal/`, incluindo `legal/java.base/LICENSE`, `legal/java.base/ASSEMBLY_EXCEPTION` e `legal/java.base/ADDITIONAL_LICENSE_INFO`, alem dos termos de cada modulo.

As fontes correspondentes, sem modificacao, sao fornecidas em `resources/third-party-sources/`. Os nomes, releases, tamanhos e hashes imutaveis estao em `third-party-sources.lock.json`. Distribua estes arquivos junto do instalador ou ofereca aos mesmos destinatarios acesso equivalente as copias hospedadas por voce pelo mesmo canal de distribuicao. Nao remova as fontes de um artefato antes de redistribui-lo. Um link para o fornecedor, sozinho, nao e tratado aqui como substituto de fornecer as fontes correspondentes.

| Runtime | Arquivo-fonte correspondente |
|---|---|
| 8u504-b01 | `OpenJDK8U-jdk-sources_8u504b01.tar.gz` |
| 17.0.20.1+1 | `OpenJDK17U-jdk-sources_17.0.20.1_1.tar.gz` |
| 21.0.12.1+1 | `OpenJDK21U-jdk-sources_21.0.12.1_1.tar.gz` |
| 25.0.4.1+1 | `OpenJDK25U-jdk-sources_25.0.4.1_1.tar.gz` |

Referencias oficiais: [licenciamento e FAQ](https://adoptium.net/docs/faq/), [downloads](https://adoptium.net/temurin/releases/), [instaladores MSI](https://adoptium.net/pt-BR/installation/windows). As URLs exatas das releases e dos arquivos-fonte constam do manifesto de fontes.

## Microsoft

Visual C++, .NET Framework, .NET Desktop Runtime, DirectX End-User Runtimes, WebView2 e XNA sao componentes Microsoft, usados sob seus proprios termos. Preserve os instaladores originais e os termos apresentados por eles. O catalogo `prerequisites.lock.json` identifica precisamente os binarios incorporados; nao modifique esses binarios nem interprete esta lista como concessao de direitos adicionais.

Referencias oficiais: [Visual C++](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist), [.NET](https://dotnet.microsoft.com/download), [DirectX legado](https://www.microsoft.com/download/details.aspx?id=8109), [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/), [XNA 4.0 Refresh](https://www.microsoft.com/download/details.aspx?id=27598).

## Dokany

O pacote opcional de sistema de arquivos preserva seu proprio instalador e termos. Ele nao e uma dependencia universal de jogos e nao deve ser instalado sem escolha explicita.

- [Dokany 2.3.1.1000](https://github.com/dokan-dev/dokany/releases/tag/v2.3.1.1000): consulte os avisos e termos fornecidos pelo projeto [dokan-dev/dokany](https://github.com/dokan-dev/dokany).

Este pacote nao concede direitos sobre jogos, ROMs, BIOS, firmware ou outros conteudos de terceiros.
