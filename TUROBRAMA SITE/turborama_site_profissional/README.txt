TURBORAMA - SITE PROFISSIONAL DE VENDA E REDIRECIONAMENTO
==========================================================

O QUE VEM NO PACOTE
-------------------
- index.html: página principal de venda
- mobile.html: versão mobile enxuta para anúncio no celular
- redirect.html: página de redirecionamento com rastreamento UTM
- obrigado.html: página pós-compra
- termos.html: termos de uso
- politica.html: política de privacidade
- slides.html: modo apresentação/slides de venda
- ads/roteiros-videos.html: roteiros para vídeos comerciais
- config.js: arquivo principal para trocar links, preço, WhatsApp e pixels
- assets/styles.css: design visual
- assets/app.js: funções de UTM, redirecionamento, slides e analytics
- assets/logo.svg: logo temporário em SVG

COMO EDITAR
-----------
1. Abra o arquivo config.js.
2. Troque:
   - checkoutUrl
   - whatsappNumero
   - whatsappMensagem
   - demonstracaoUrl
   - precoPrincipal
   - precoParcelado
   - suporteEmail
   - googleAnalyticsId, se usar
   - metaPixelId, se usar
3. Publique todos os arquivos em sua hospedagem.

COMO COLOCAR VÍDEOS
-------------------
1. Crie uma pasta: assets/videos/
2. Coloque os arquivos, por exemplo:
   assets/videos/apresentacao.mp4
   assets/videos/demo.mp4
3. No config.js, altere:
   videoApresentacao: "assets/videos/apresentacao.mp4"
   videoDemo: "assets/videos/demo.mp4"

VÍDEOS DE VENDA
----------------
Use vídeos curtos com apresentador, narrador, avatar ou gravação de tela para explicar o produto, mostrar benefícios, suporte, garantia e chamada para compra.

COMO PUBLICAR
-------------
Hostinger/cPanel:
- Envie todos os arquivos para public_html.

Netlify:
- Arraste a pasta do site no painel do Netlify.

GitHub Pages:
- Envie os arquivos para um repositório e ative Pages.

ANÚNCIOS
--------
Use o link da página principal com UTMs, exemplo:
https://seudominio.com/?utm_source=meta&utm_medium=cpc&utm_campaign=turborama_lancamento

O site preserva esses parâmetros quando o usuário clica em checkout, WhatsApp ou demo.


COMENTÁRIOS NO SITE
-------------------
A seção de comentários foi criada com visual de rede social e carregada pelo arquivo config.js. Edite nomes, textos, notas, horários e respostas diretamente no config.js.

VERSÃO MOBILE
-------------
O site principal já é responsivo, e o pacote também inclui mobile.html, uma versão mais direta para tráfego pago em celular.
Você pode anunciar direto para:
https://seudominio.com/mobile.html?utm_source=meta&utm_medium=cpc&utm_campaign=turborama_mobile

SLIDES MOBILE AUTOMÁTICOS
-------------------------
A versão mobile agora tem carrossel automático de imagens e vídeos.

Pasta criada para os arquivos dos slides:
assets/slides/

Dentro dela já existem 3 imagens de exemplo:
- assets/slides/slide-01.svg
- assets/slides/slide-02.svg
- assets/slides/slide-03.svg

Para trocar:
1. Coloque suas imagens ou vídeos dentro de assets/slides/.
2. Abra config.js.
3. Edite a lista slidesMobile.

Exemplo com imagem:
{ tipo: "imagem", arquivo: "assets/slides/minha-foto.jpg", etiqueta: "Sistema", titulo: "Visual premium", texto: "Texto do slide." }

Exemplo com vídeo:
{ tipo: "video", arquivo: "assets/slides/meu-video.mp4", etiqueta: "Demo", titulo: "Sistema em movimento", texto: "Vídeo curto para venda." }

Tamanho recomendado:
- Imagem vertical: 1080 x 1680 px ou 1080 x 1920 px
- Vídeo vertical: MP4, 1080 x 1920 px, 8 a 20 segundos
