(function(){
  const cfg = window.TURBORAMA_CONFIG || {};

  function qs(name){ return new URLSearchParams(location.search).get(name); }

  function captureUtm(){
    const keys = ['utm_source','utm_medium','utm_campaign','utm_term','utm_content','gclid','fbclid'];
    const data = {};
    keys.forEach(k => { const v = qs(k); if(v) data[k] = v; });
    if(Object.keys(data).length){
      data.capturadoEm = new Date().toISOString();
      localStorage.setItem('turborama_utm', JSON.stringify(data));
    }
  }

  function getUtmString(){
    try{
      const data = JSON.parse(localStorage.getItem('turborama_utm') || '{}');
      const params = new URLSearchParams(data);
      return params.toString();
    }catch(e){ return ''; }
  }

  function whatsappUrl(){
    const msg = encodeURIComponent(cfg.whatsappMensagem || 'Olá! Quero conhecer o TurboRama.');
    return `https://wa.me/${cfg.whatsappNumero || '5599999999999'}?text=${msg}`;
  }

  function withUtm(url){
    if(!url) return '#';
    const utm = getUtmString();
    if(!utm) return url;
    return url + (url.includes('?') ? '&' : '?') + utm;
  }

  function bindLinks(){
    document.querySelectorAll('[data-dest]').forEach(el => {
      const dest = el.getAttribute('data-dest');
      el.setAttribute('href', `redirect.html?dest=${encodeURIComponent(dest)}`);
    });
    document.querySelectorAll('[data-config-text]').forEach(el => {
      const key = el.getAttribute('data-config-text');
      if(cfg[key]) el.textContent = cfg[key];
    });
  }

  function loadTracking(){
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
      !function(f,b,e,v,n,t,s){if(f.fbq)return;n=f.fbq=function(){n.callMethod?
      n.callMethod.apply(n,arguments):n.queue.push(arguments)};if(!f._fbq)f._fbq=n;
      n.push=n;n.loaded=!0;n.version='2.0';n.queue=[];t=b.createElement(e);t.async=!0;
      t.src=v;s=b.getElementsByTagName(e)[0];s.parentNode.insertBefore(t,s)}(window, document,'script',
      'https://connect.facebook.net/en_US/fbevents.js');
      fbq('init', cfg.metaPixelId);
      fbq('track', 'PageView');
    }
  }

  window.TurboRamaSite = { withUtm, whatsappUrl, getUtmString };
  captureUtm();
  bindLinks();
  loadTracking();
})();
