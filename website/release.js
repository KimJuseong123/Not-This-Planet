(() => {
  const galleryImage = document.getElementById('galleryImage');
  const galleryVideo = document.getElementById('galleryVideo');
  const thumbs = [...document.querySelectorAll('.gallery-thumb')];
  function selectMedia(button) {
    galleryVideo.pause();
    const isVideo = button.hasAttribute('data-gallery-video');
    galleryVideo.hidden = !isVideo;
    galleryImage.hidden = isVideo;
    if (!isVideo) {
      galleryImage.src = button.dataset.gallerySrc;
      galleryImage.alt = button.dataset.galleryLabel;
    }
    thumbs.forEach(thumb => thumb.setAttribute('aria-pressed', String(thumb === button)));
    document.getElementById('galleryCaption').textContent = button.dataset.galleryLabel;
  }
  thumbs.forEach((button, index) => {
    button.addEventListener('click', () => selectMedia(button));
    button.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const next = event.key === 'Home' ? 0 : event.key === 'End' ? thumbs.length - 1 : (index + (event.key === 'ArrowRight' ? 1 : -1) + thumbs.length) % thumbs.length;
      thumbs[next].focus();
      selectMedia(thumbs[next]);
    });
  });
  const config = window.ReleaseConfig || {};
  // A missing/invalid override keeps the working HTML fallback intact.
  try {
    const feedbackUrl = new URL(String(config.feedbackUrl || ''));
    if (feedbackUrl.protocol === 'https:' && !feedbackUrl.username && !feedbackUrl.password) {
      document.querySelectorAll('[data-feedback-link]').forEach(button => {
        button.href = feedbackUrl.href;
      });
    }
  } catch (_) { /* Keep the default public survey link. */ }
  const link = document.getElementById('downloadLink');
  const raw = String(config.downloadUrl || '').trim();
  let valid = false;
  try {
    const url = new URL(raw, location.href);
    valid = !!raw && !raw.startsWith('#') && (url.protocol === 'https:' || url.protocol === 'http:' || (location.protocol === 'file:' && url.protocol === 'file:'));
  } catch (_) { /* Unconfigured or malformed links remain disabled. */ }
  if (valid) {
    link.href = raw;
    link.hidden = false;
    document.getElementById('downloadPending').hidden = true;
    document.getElementById('downloadStatus').textContent = '다운로드 후 ZIP 파일 전체를 압축 해제해 주세요.';
  }
  document.getElementById('buildMeta').textContent = [config.version, config.fileSize].filter(Boolean).join(' · ');
  const dialog = document.getElementById('mediaDialog');
  const preview = document.getElementById('previewImage');
  let returnFocus;
  document.querySelectorAll('[data-preview]').forEach(button => {
    button.addEventListener('click', () => {
      const img = button.querySelector('img');
      returnFocus = button;
      preview.src = img.getAttribute('src');
      preview.alt = img.alt;
      document.getElementById('previewTitle').textContent = img.alt;
      dialog.showModal();
    });
  });
  dialog.querySelector('button').addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', event => {
    const box = dialog.getBoundingClientRect();
    if (event.target === dialog && (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom)) dialog.close();
  });
  dialog.addEventListener('close', () => { preview.removeAttribute('src'); returnFocus?.focus(); });
})();
