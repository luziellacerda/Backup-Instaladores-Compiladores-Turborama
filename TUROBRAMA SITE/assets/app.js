(function(){
  const cfg = window.TURBORAMA_CONFIG || {};
  const $ = (sel, root=document) => root.querySelector(sel);
  const $$ = (sel, root=document) => [...root.querySelectorAll(sel)];

  function encodeParams(params){
    const out = new URLSearchParams();
    Object.entries(params || {}).forEach(([k,v])=>{ if(v) out.set(k,v); });
    return out.toString();
  }

  function captureUtm(){
    const url = new URL(location.href);
    const keys = ['utm_source','utm_medium','utm_campaign','utm_term','utm_content','fbclid','gclid'];
    const data = {};
    keys.forEach(k => { if(url.searchParams.get(k)) data[k] = url.searchParams.get(k); });
    if(Object.keys(data).length){
      localStorage.setItem('turborama_utm', JSON.stringify(data));
    }
  }

  function getUtm(){
    try { return JSON.parse(localStorage.getItem('turborama_utm') || '{}'); } catch(e){ return {}; }
  }

  function withUtm(url){
    if(!url || url === '#') return '#';
    const utm = getUtm();
    const hasData = Object.keys(utm).length > 0;
    if(!hasData) return url;
    try{
      const u = new URL(url, location.href);
      Object.entries(utm).forEach(([k,v]) => u.searchParams.set(k,v));
      return u.toString();
    }catch(e){
      const sep = url.includes('?') ? '&' : '?';
      return url + sep + encodeParams(utm);
    }
  }

  function applyConfig(){
    $$('[data-brand]').forEach(el => el.textContent = cfg.marca || 'TurboRama');
    $$('[data-subtitle]').forEach(el => el.textContent = cfg.subtitulo || 'Sistema premium');
    $$('[data-price]').forEach(el => el.textContent = cfg.precoPrincipal || 'Consulte');
    $$('[data-parcel]').forEach(el => el.textContent = cfg.precoParcelado || '');
    $$('[data-offer]').forEach(el => el.textContent = cfg.etiquetaOferta || 'Licença digital');
    $$('[data-guarantee]').forEach(el => el.textContent = cfg.garantiaDias || '7 dias');
    $$('[data-legal]').forEach(el => el.textContent = cfg.avisoLegal || '');
    $$('[data-email]').forEach(el => { el.textContent = cfg.suporteEmail || ''; el.href = 'mailto:' + (cfg.suporteEmail || ''); });

    const waText = encodeURIComponent(cfg.whatsappMensagem || 'Olá! Tenho interesse no TurboRama.');
    const wa = `https://wa.me/${cfg.whatsappNumero || ''}?text=${waText}`;
    $$('[data-link="whatsapp"]').forEach(el => el.href = `redirect.html?to=whatsapp&url=${encodeURIComponent(wa)}`);
    $$('[data-link="checkout"]').forEach(el => el.href = `redirect.html?to=checkout&url=${encodeURIComponent(cfg.checkoutUrl || '#')}`);
    $$('[data-link="demo"]').forEach(el => el.href = `redirect.html?to=demo&url=${encodeURIComponent(cfg.demonstracaoUrl || '#')}`);

    if(cfg.videoApresentacao){
      const v = $('#video-apresentacao');
      if(v){ v.innerHTML = `<video src="${cfg.videoApresentacao}" controls playsinline preload="metadata" style="width:100%;height:100%;display:block;background:#000"></video>`; }
    }
    if(cfg.videoDemo){
      const v = $('#video-demo');
      if(v){ v.innerHTML = `<video src="${cfg.videoDemo}" controls playsinline preload="metadata" style="width:100%;height:100%;display:block;background:#000"></video>`; }
    }
  }

  function slides(){
    const items = $$('.slide');
    if(!items.length) return;
    let i = 0;
    const show = (n) => {
      i = (n + items.length) % items.length;
      items.forEach((el,idx) => el.classList.toggle('active', idx === i));
    };
    $('#slide-next')?.addEventListener('click', () => show(i+1));
    $('#slide-prev')?.addEventListener('click', () => show(i-1));
    setInterval(()=>show(i+1), 6500);
  }



  function renderMobileSlides(){
    const carousel = $('#mobile-media-carousel');
    const stage = $('#mobile-media-stage');
    const dotsRoot = $('#mobile-slide-dots');
    const title = $('#mobile-slide-title');
    const textEl = $('#mobile-slide-text');
    const kicker = $('#mobile-slide-kicker');
    if(!carousel || !stage || !dotsRoot) return;

    const slides = Array.isArray(cfg.slidesMobile) && cfg.slidesMobile.length ? cfg.slidesMobile : [
      {tipo:'imagem', arquivo:'assets/slides/slide-01.svg', titulo:'Biblioteca visual', texto:'Mostre a organização, capas e categorias do TurboRama.', etiqueta:'Slide 1'},
      {tipo:'imagem', arquivo:'assets/slides/slide-02.svg', titulo:'Tela de venda', texto:'Apresente o sistema com visual forte para anúncio mobile.', etiqueta:'Slide 2'},
      {tipo:'imagem', arquivo:'assets/slides/slide-03.svg', titulo:'Suporte e instalação', texto:'Explique compra, suporte e atendimento direto pelo WhatsApp.', etiqueta:'Slide 3'}
    ];

    let index = 0;
    let timer = null;
    let startedX = 0;
    let dragging = false;
    const interval = Number(cfg.slidesMobileIntervalo || 5200);

    function build(){
      stage.innerHTML = slides.map((s, i) => {
        const file = s.arquivo || '';
        const isVideo = (s.tipo || '').toLowerCase() === 'video' || /\.(mp4|webm|ogg)$/i.test(file);
        const media = isVideo
          ? `<video src="${file}" muted playsinline preload="metadata"></video>`
          : `<img src="${file}" alt="${s.titulo || 'Slide TurboRama'}" loading="${i === 0 ? 'eager' : 'lazy'}">`;
        return `<article class="mobile-media-slide" data-index="${i}" data-video="${isVideo ? '1' : '0'}">${media}</article>`;
      }).join('');
      dotsRoot.innerHTML = slides.map((_, i) => `<button type="button" aria-label="Ir para slide ${i+1}" data-dot="${i}"></button>`).join('');
      $$('[data-dot]', dotsRoot).forEach(btn => btn.addEventListener('click', () => show(Number(btn.dataset.dot), true)));
    }

    function stopVideos(){
      $$('video', stage).forEach(v => { try { v.pause(); v.currentTime = 0; } catch(e){} });
    }

    function show(n, manual){
      index = (n + slides.length) % slides.length;
      const current = slides[index] || {};
      $$('.mobile-media-slide', stage).forEach((el, i) => el.classList.toggle('active', i === index));
      $$('[data-dot]', dotsRoot).forEach((el, i) => el.classList.toggle('active', i === index));
      if(title) title.textContent = current.titulo || 'TurboRama';
      if(textEl) textEl.textContent = current.texto || '';
      if(kicker) kicker.textContent = current.etiqueta || `Slide ${index + 1}`;
      stopVideos();
      const active = $(`.mobile-media-slide[data-index="${index}"]`, stage);
      const video = active?.querySelector('video');
      if(video){
        video.onended = () => show(index + 1);
        video.play().catch(()=>{});
      }
      if(manual) restart();
    }

    function next(){ show(index + 1); }
    function prev(){ show(index - 1, true); }
    function restart(){ clearInterval(timer); timer = setInterval(next, interval); }

    $('#mobile-slide-next')?.addEventListener('click', () => { show(index + 1, true); });
    $('#mobile-slide-prev')?.addEventListener('click', prev);
    carousel.addEventListener('mouseenter', () => clearInterval(timer));
    carousel.addEventListener('mouseleave', restart);
    carousel.addEventListener('touchstart', e => { startedX = e.touches[0].clientX; dragging = true; clearInterval(timer); }, {passive:true});
    carousel.addEventListener('touchmove', e => {
      if(!dragging) return;
      const dx = e.touches[0].clientX - startedX;
      if(Math.abs(dx) > 55){ dragging = false; dx < 0 ? show(index + 1, true) : show(index - 1, true); }
    }, {passive:true});
    carousel.addEventListener('touchend', () => { dragging = false; restart(); }, {passive:true});

    build();
    show(0);
    restart();
  }

  function renderComments(){
    const root = $('#comentarios-lista');
    if(!root) return;
    const comments = Array.isArray(cfg.comentarios) ? cfg.comentarios : [];
    if(!comments.length){
      root.innerHTML = '<div class="comment empty">Nenhum comentário configurado ainda.</div>';
      return;
    }
    root.innerHTML = comments.map((c, idx) => {
      const stars = '★★★★★'.slice(0, Math.max(0, Math.min(5, Number(c.nota || 5))));
      const avatar = (c.avatar || c.nome || '?').slice(0,1).toUpperCase();
      const resposta = c.resposta ? `<div class="seller-reply"><strong>TurboRama respondeu</strong><p>${c.resposta}</p></div>` : '';
      return `<article class="comment" style="--delay:${idx * 70}ms">
        <div class="comment-avatar">${avatar}</div>
        <div class="comment-body">
          <div class="comment-head"><strong>${c.nome || 'Cliente'}</strong><span>${c.tempo || 'comentário'}</span></div>
          <div class="comment-stars">${stars}</div>
          <p>${c.texto || ''}</p>
          ${resposta}
        </div>
      </article>`;
    }).join('');
  }

  function analytics(){
    if(cfg.googleAnalyticsId){
      const s = document.createElement('script');
      s.async = true;
      s.src = `https://www.googletagmanager.com/gtag/js?id=${cfg.googleAnalyticsId}`;
      document.head.appendChild(s);
      window.dataLayer = window.dataLayer || [];
      function gtag(){dataLayer.push(arguments)}
      window.gtag = gtag;
      gtag('js', new Date());
      gtag('config', cfg.googleAnalyticsId);
    }
    if(cfg.metaPixelId){
      !(function(f,b,e,v,n,t,s){if(f.fbq)return;n=f.fbq=function(){n.callMethod?
      n.callMethod.apply(n,arguments):n.queue.push(arguments)};if(!f._fbq)f._fbq=n;
      n.push=n;n.loaded=!0;n.version='2.0';n.queue=[];t=b.createElement(e);t.async=!0;
      t.src=v;s=b.getElementsByTagName(e)[0];s.parentNode.insertBefore(t,s)})(window, document,'script','https://connect.facebook.net/en_US/fbevents.js');
      fbq('init', cfg.metaPixelId); fbq('track', 'PageView');
    }
  }

  captureUtm();
  applyConfig();
  slides();
  renderMobileSlides();
  renderComments();
  analytics();
  window.TurboRamaUtils = { getUtm, withUtm };
})();
