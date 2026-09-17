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
    document.getElementById('downloadStatus').textContent = '받은 실행 파일을 빈 폴더에 두고 실행해 주세요.';
  }
  // 크기는 **런처**의 것이다(15 MB). 런처가 그다음에 받는 게임은 zip 156 MB ·
  // 풀면 372 MB(매니페스트 `totalBytes`)라 그 값으로 대신할 수 없다 —
  // 사람이 받는 파일은 런처이므로 이 값만 손으로 적는다.
  const buildMeta = document.getElementById('buildMeta');
  buildMeta.textContent = String(config.fileSize || '').trim();

  // **버전은 손으로 적지 않는다** (S15P21B202-595). 전에는 `release-config.js` 에
  // 적어 두고 릴리스마다 고쳐야 했는데, 그 일이 어디에도 적혀 있지 않아 v1.1.0 이
  // 나간 지 사흘이 지나도록 사이트는 v1.0.0 이라고 말하고 있었다.
  //
  // `latest.json` 은 `release-prod` 가 옮기는 바로 그 파일이다 — 사용자가 받는 것이
  // 무엇인지 아는 **유일한 진실**이고, 그것을 읽으면 영영 어긋나지 않는다.
  //
  // **못 읽어도 화면은 그대로다.** 다운로드 버튼·안내는 위에서 이미 세워 뒀고,
  // 여기서는 버전 한 줄만 못 채운다. 서버가 잠깐 안 되는 것으로 받는 길이
  // 막히면 안 된다(`feedbackUrl`·`downloadUrl` 을 다루는 방식과 같은 결).
  fetch('/builds/latest.json', { cache: 'no-store' })
    .then(response => (response.ok ? response.json() : null))
    .then(latest => {
      const version = latest && typeof latest.version === 'string' ? latest.version.trim() : '';
      if (!version) return;
      buildMeta.textContent = [version, config.fileSize].filter(Boolean).join(' · ');
      const heading = document.getElementById('notesVersion');
      if (heading) heading.textContent = version;
    })
    .catch(() => { /* 버전 한 줄이 비는 것뿐이다. 받는 길은 위에서 이미 열려 있다. */ });

  // **이번 버전에서 바뀐 것** (S15P21B202-462 의 마지막 완료 조건).
  //
  // 문구는 **사람이 넣는다.** 릴리스가 만드는 것은 초안이고(`release_summary.py`),
  // AI 가 쓴 문장이 그대로 나가면 이상한 한 줄이 게임의 첫인상이 된다.
  // 비어 있으면 그 자리를 **아예 그리지 않는다** — 빈 제목만 남으면 고장으로 보인다.
  const notes = Array.isArray(config.notes) ? config.notes.filter(n => String(n).trim()) : [];
  const notesSection = document.getElementById('notes');
  if (notesSection && notes.length) {
    const list = document.getElementById('notesList');
    notes.forEach(note => {
      const item = document.createElement('li');
      item.textContent = String(note).trim();   // textContent — 넣은 글이 마크업이 되지 않게
      list.appendChild(item);
    });
    notesSection.hidden = false;
  }
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
