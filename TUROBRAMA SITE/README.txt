TURBORAMA - SITE PROFISSIONAL DE VENDA EM PÁGINA ÚNICA

ARQUIVO PRINCIPAL
- index.html

O site agora usa UMA página principal responsiva.
Não precisa usar mobile.html nem slides.html. O index.html faz as duas coisas:
- versão desktop
- versão mobile
- slides automáticos de imagem e vídeo
- comentários
- vídeos
- planos
- checkout
- WhatsApp
- redirecionamento com UTM

PASTA DOS SLIDES
Coloque os arquivos dos slides em:
assets/slides/

Aceita:
- .jpg
- .jpeg
- .png
- .webp
- .svg
- .mp4
- .webm
- .ogg

CONFIGURAÇÃO
Abra o arquivo:
config.js

Edite principalmente:
- checkoutUrl
- whatsappNumero
- whatsappMensagem
- demonstracaoUrl
- precoPrincipal
- precoParcelado
- comentarios
- slidesMobile
- videoApresentacao
- videoDemo

COMO COLOCAR SLIDE
1. Copie a imagem ou vídeo para assets/slides/
2. Abra config.js
3. Adicione na lista slidesMobile:

{ tipo: "imagem", arquivo: "assets/slides/slide-novo.jpg", etiqueta: "Sistema", titulo: "Título do slide", texto: "Texto do slide." }

Para vídeo:

{ tipo: "video", arquivo: "assets/slides/video-01.mp4", etiqueta: "Demo", titulo: "Vídeo do sistema", texto: "Demonstração rápida." }

PUBLICAÇÃO
Envie os arquivos para a raiz da hospedagem.
A página principal será: seudominio.com/index.html

OBSERVAÇÃO
As páginas termos.html e politica.html são auxiliares legais. A página comercial/de venda é apenas o index.html.
