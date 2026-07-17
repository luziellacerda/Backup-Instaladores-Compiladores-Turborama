/* =========================================================
   TURBORAMA - CONFIGURAÇÃO DO SITE
   Troque os valores abaixo antes de publicar.
   ========================================================= */
window.TURBORAMA_CONFIG = {
  marca: "TurboRama",
  subtitulo: "Sistema premium para organizar, apresentar e vender sua experiência retrô",

  // Links principais
  checkoutUrl: "https://SEU-CHECKOUT-AQUI.com/produto/turborama",
  whatsappNumero: "5599999999999",
  whatsappMensagem: "Olá! Tenho interesse no sistema TurboRama. Quero saber planos, instalação e suporte.",
  demonstracaoUrl: "https://SEU-LINK-DE-DEMO.com",
  suporteEmail: "contato@seudominio.com.br",

  // Oferta
  precoPrincipal: "R$ 197",
  precoParcelado: "ou 12x de R$ 19,70",
  garantiaDias: "7 dias",
  etiquetaOferta: "Licença digital + suporte de instalação",

  // Comentários exibidos na página. Edite nomes, textos, notas e respostas abaixo.
  comentarios: [
    { nome: "Marcelo A.", tempo: "agora", avatar: "M", nota: 5, texto: "Gostei da apresentação do TurboRama. Ficou fácil entender o que vem no pacote e como funciona o suporte.", resposta: "Obrigado! Antes da compra mostramos a demonstração e tiramos dúvidas pelo WhatsApp." },
    { nome: "Rafael S.", tempo: "agora", avatar: "R", nota: 5, texto: "A página passa mais confiança do que só mandar um link seco. O visual ficou bem profissional.", resposta: "Essa é a ideia: explicar, mostrar a oferta e levar para checkout ou atendimento." },
    { nome: "Daniel P.", tempo: "agora", avatar: "D", nota: 5, texto: "O que gostei foi ter vídeo, planos e garantia bem explicados antes de comprar.", resposta: "Sim. A página foi feita para explicar planos, vídeo, garantia e atendimento antes da compra." },
    { nome: "Carlos M.", tempo: "pergunta", avatar: "C", nota: 4, texto: "Consigo chamar no WhatsApp antes de pagar para tirar dúvidas da instalação?", resposta: "Consegue. O botão de WhatsApp preserva a campanha do anúncio e abre a conversa com mensagem pronta." }
  ],

  // Slides mobile automáticos. Coloque imagens ou vídeos dentro da pasta assets/slides/.
  // Formatos aceitos: imagem .jpg .png .webp .svg | vídeo .mp4 .webm .ogg
  slidesMobileIntervalo: 5200,
  slidesMobile: [
    { tipo: "imagem", arquivo: "assets/slides/slide-01.svg", etiqueta: "Organização", titulo: "Biblioteca bonita e organizada", texto: "Mostre o visual do sistema, capas, categorias e navegação de forma rápida no celular." },
    { tipo: "imagem", arquivo: "assets/slides/slide-02.svg", etiqueta: "Venda", titulo: "Apresentação com aparência premium", texto: "Slide ideal para explicar a proposta do TurboRama antes do cliente clicar em comprar." },
    { tipo: "imagem", arquivo: "assets/slides/slide-03.svg", etiqueta: "Atendimento", titulo: "Compra, suporte e instalação", texto: "Use o slide para reforçar WhatsApp, suporte e processo de instalação." }
    // Para adicionar vídeo:
    // { tipo: "video", arquivo: "assets/slides/video-01.mp4", etiqueta: "Vídeo", titulo: "Demonstração em movimento", texto: "Vídeo curto mostrando o sistema funcionando." }
  ],

  // Vídeos: coloque seus arquivos em assets/videos/ e atualize aqui.
  videoApresentacao: "",
  videoDemo: "",

  // Analytics / pixels. Deixe vazio se não usar.
  googleAnalyticsId: "",
  metaPixelId: "",

  // Aviso importante: mantenha para evitar problema legal com anúncios.
  avisoLegal: "O TurboRama não vende, distribui nem acompanha jogos, ROMs, BIOS ou conteúdo protegido. O sistema é uma solução visual/organizacional. O usuário deve utilizar apenas arquivos e conteúdos que possua autorização legal para usar.",

  // Ative/desative blocos
  mostrarProvaSocial: true,
  mostrarBlocoLegal: true,
  mostrarPlanos: true
};
