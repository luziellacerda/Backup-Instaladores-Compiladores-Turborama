SITE TURBORAMA - LANDING PAGE DE VENDA E REDIRECIONAMENTO

ARQUIVOS PRINCIPAIS
- index.html: página principal de venda.
- redirect.html: página de redirecionamento para checkout, WhatsApp, demonstração e suporte.
- obrigado.html: página de obrigado.
- termos.html: termos de uso.
- politica.html: política de privacidade.
- config.js: arquivo central para editar links, preço, WhatsApp e rastreamento.
- assets/css/style.css: visual do site.
- assets/js/main.js: captura de UTM, redirecionamento e pixels.
- assets/img/logo.svg: logo provisória editável.

COMO EDITAR OS LINKS
Abra o arquivo config.js e altere:
- checkoutUrl: coloque seu link real de pagamento.
- whatsappNumero: coloque seu número com DDI e DDD. Exemplo: 5582999999999.
- whatsappMensagem: mensagem automática do WhatsApp.
- demonstracaoUrl: link de vídeo, página ou apresentação.
- suporteUrl: link de suporte.
- precoPrincipal: preço exibido no site.
- googleAnalyticsId: ID do Google Analytics, se tiver.
- metaPixelId: ID do Meta Pixel, se tiver.

COMO USAR EM ANÚNCIOS
Use o link da página index.html como destino dos seus anúncios.
Exemplo:
https://seudominio.com/?utm_source=facebook&utm_medium=cpc&utm_campaign=turborama_lancamento

O site salva os parâmetros UTM no navegador e tenta manter esses dados quando o cliente for ao checkout.

AVISO IMPORTANTE
O site já informa que o TurboRama não inclui jogos, ROMs ou conteúdos de terceiros.
Use apenas arquivos próprios, autorizados ou backups legais.

COMO PUBLICAR
Opção simples:
1. Suba todos os arquivos para uma hospedagem, Netlify, Vercel ou GitHub Pages.
2. Edite config.js antes de publicar.
3. Teste os botões Comprar, WhatsApp e Demonstração.
