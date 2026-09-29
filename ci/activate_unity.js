#!/usr/bin/env node
// =====================================================================
//  activate_unity.js — activation de licence Unity PERSONAL automatisée
//  pour le CI (Codemagic), sans intervention humaine.
//
//  Le portail license.unity3d.com est devenu une SPA (Vue) avec
//  connexion via OAuth sur login.unity.com. Ce script gère :
//   - la chaîne de redirection /manual → login.unity.com (wizard
//     e-mail PUIS mot de passe, en 2 écrans distincts) ;
//   - l'éventuel écran 2FA (TOTP si UNITY_TOTP_KEY fourni) ;
//   - l'upload du .alf (input #licenseFile, le bouton ne s'active
//     qu'APRÈS que Vue a validé le contenu du fichier) ;
//   - l'étape 2 : la tuile « Personal » est MASQUÉE en CSS par Unity
//     (display:none) — on retire la propriété puis on clique, c'est
//     le contournement GameCI encore valable en 2025/2026 ;
//   - le choix de capacité, la validation et le téléchargement du .ulf
//     (lien <a download> → intercepté par CDP) ;
//   - une capture d'écran numérotée à CHAQUE étape (01-...png) — si
//     Unity change une page, la capture montre exactement quoi fixer ;
//   - diagnostic en cas d'échec (error.png + error.html).
//
//  Usage : node activate_unity.js EMAIL MOT_DE_PASSE /chemin/unity.alf [CLE_TOTP]
// =====================================================================

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const puppeteer = require('puppeteer');

const OUT = process.cwd(); // le dossier où tomberont le .ulf et les captures

async function sleep(ms) {
  return new Promise((res) => setTimeout(res, ms));
}

// --- TOTP minimal (double authentification, optionnel) ----------------
function totp(secretBase32) {
  const clean = secretBase32.replace(/[\s-]/g, '').toUpperCase();
  const buf = Buffer.from(clean, 'base32');
  const counter = Math.floor(Date.now() / 1000 / 30);
  const buf8 = Buffer.alloc(8);
  buf8.writeUInt32BE(Math.floor(counter / 2 ** 32), 0);
  buf8.writeUInt32BE(counter % 2 ** 32, 4);
  const hmac = crypto.createHmac('sha1', buf).update(buf8).digest();
  const off = hmac[19] & 0xf;
  const code = ((hmac[off] & 0x7f) << 24 | hmac[off + 1] << 16 |
    hmac[off + 2] << 8 | hmac[off + 3]) % 1000000;
  return String(code).padStart(6, '0');
}

// --- capture d'écran numérotée (jamais fatale) ------------------------
async function shot(page, name) {
  try {
    await page.screenshot({ path: path.join(OUT, name), fullPage: true });
    console.log(`[SHOT] ${name}`);
  } catch (e) {
    console.log(`[SHOT] échec ${name} : ${e.message}`);
  }
}

// --- premier sélecteur présent parmi une liste (avec temps limite) ----
async function waitForFirst(page, selectors, timeout) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    for (const s of selectors) {
      if (await page.$(s)) return s;
    }
    await sleep(400);
  }
  return null;
}

// --- validation d'un formulaire : bouton générique, sinon Entrée ------
async function submit(page) {
  const sub = await waitForFirst(page,
    ['button[type="submit"]', 'input[type="submit"]',
      'button[name="commit"]', 'input[name="commit"]'], 4000);
  if (sub) {
    await page.click(sub).catch(() => {});
  } else {
    await page.keyboard.press('Enter').catch(() => {});
  }
}

// ---- CLIQUE le premier bouton ACTIVÉ et visible de la page ----------
// (le portail Vue désactive le bouton tant que la validation n'est pas
//  faite : submitDisabled = !isValidated || isPosting). On balaie les
//  boutons/lien dans l'ordre du DOM et on clique le premier prêt.
async function clickReadyButton(page) {
  return page.evaluate(() => {
    const cand = Array.from(document.querySelectorAll(
      'button, input[type="submit"], a.btn, input[class*="btn"]'));
    const prêts = [];
    for (const el of cand) {
      if (el.disabled) continue;
      const st = window.getComputedStyle(el);
      if (st.display === 'none' || st.visibility === 'hidden' ||
        el.getAttribute('aria-hidden') === 'true') continue;
      const r = el.getBoundingClientRect();
      if (r.width < 5 || r.height < 5) continue;
      if (el.getAttribute('aria-disabled') === 'true') continue;
      // on ignore les liens de déconnexion / aide du portail
      const t = (el.textContent || el.value || '').trim().toLowerCase();
      if (/log ?out|se déconnecter|help centre|support/.test(t)) continue;
      // on ne clique jamais les boutons « retour » (risque de boucle)
      if (/back|previous|retour|annul|cancel/.test(t)) continue;
      prêts.push(el);
    }
    // 1er passage : les boutons clairement « avancer » (évite de tomber
    // sur un bouton « Deny » d'un écran de consentement OAuth)
    const AVANCE = /next|suivant|continue|valider|envoyer|submit|authorize|autoriser|allow|accepter|accept|agree|j'accepte|commencer|download|télécharger|confirm|ok/i;
    const primaire = prêts.find((el) =>
      AVANCE.test((el.textContent || el.value || '')));
    const cible = primaire || prêts[0];
    if (!cible) return null;
    const txt = (cible.textContent || cible.value || '').trim().toLowerCase();
    cible.click();
    return txt || '(bouton sans texte)';
  });
}

// ---- DÉ-MASQUE + clique la tuile/option « Personal » de l'étape 2 ----
// Unity cache volontairement l'option Personal (style display:none) ;
// GameCI dé-clique la propriété sur l'élément et tous ses ancêtres.
async function selectPersonal(page) {
  return page.evaluate(() => {
    // 1) ciblage par les sélecteurs historiques s'ils existent encore
    let target = document.querySelector(
      'input[id="type_personal"][value="personal"], input[value="personal"],' +
      ' input[id="type_personal"], [data-test*="personal" i],' +
      ' [class*="personal" i] input, input[name*="personal" i]');
    // 2) sinon : tout élément VISUEL contenant le texte « Personal »
    if (!target) {
      const items = Array.from(document.querySelectorAll(
        'label, div, span, h2, h3, p, button, a'));
      const hit = items.find((el) => {
        const t = (el.textContent || '').trim();
        return /^personal$/i.test(t) || /^(personal|personnalisée?)\b/i.test(t) &&
          el.textContent && el.textContent.trim().length < 60;
      });
      if (hit) target = hit;
    }
    if (!target) return null;
    // supprime le masquage sur l'élément ET toute sa lignée d'ancêtres
    let node = target;
    while (node && node !== document.body) {
      if (node.style && node.style.display === 'none') node.style.display = '';
      if (node.style && node.style.visibility === 'hidden') {
        node.style.visibility = '';
      }
      if (node.classList && node.classList.contains('hide')) {
        node.classList.remove('hide');
      }
      node = node.parentElement;
    }
    // clique (le vrai contrôle est parfois parent ou enfant du trouvé)
    const clickEl = target.matches('input, button, a') ? target :
      (target.querySelector('input, button, a') || target);
    clickEl.click();
    return (target.textContent || target.value || '').trim().slice(0, 40);
  });
}

// ---- clique l'option de CAPACITÉ « Personal only » (étape 2, suite) ---
async function selectCapacity(page) {
  return page.evaluate(() => {
    let target = document.querySelector(
      'input[id="option3"][name="personal_capacity"], input[name="personal_capacity"],' +
      ' input[name*="capacity" i], input[name*="seat" i]');
    if (!target) {
      const items = Array.from(document.querySelectorAll('label, div, p, span'));
      const hit = items.find((el) =>
        /personal\s*only|no purchase|unlimited|capacité/i.test(el.textContent || '') &&
        (el.textContent || '').trim().length < 120);
      if (hit) target = hit.querySelector('input, button') || hit;
    }
    if (!target) return null;
    let node = target;
    while (node && node !== document.body) {
      if (node.style && node.style.display === 'none') node.style.display = '';
      node = node.parentElement;
    }
    const clickEl = target.matches('input, button, a') ? target :
      (target.querySelector('input, button, a') || target);
    clickEl.click();
    return 'capacité cliquée';
  });
}

// ---- saisit le champ 2FA et valide (TOTP ou code par e-mail) ---------
async function fillTwoFactor(page, kind, code) {
  const field = kind === 'totp'
    ? 'input[name="conversations_tfa_required_form[verify_code]"]'
    : 'input[name="conversations_email_tfa_required_form[code]"]';
  const sel = await page.$(field);
  if (!sel) return false;
  await page.click(field);
  await page.type(field, code, { delay: 30 });
  await page.keyboard.press('Enter').catch(() => {});
  await submit(page);
  return true;
}

async function main() {
  const [email, password, alf, authenticatorKey] = process.argv.slice(2);
  if (!email || !password || !alf) {
    console.log('[ERREUR] usage : node activate_unity.js EMAIL MOT_DE_PASSE fichier.alf [cleTOTP]');
    process.exit(2);
  }
  if (!fs.existsSync(alf)) {
    console.log(`[ERREUR] fichier .alf introuvable : ${alf}`);
    process.exit(2);
  }

  const browser = await puppeteer.launch({
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });
  const page = await browser.newPage();

  // autoriser le téléchargement du .ulf dans le dossier courant
  const downloadPath = OUT;
  try {
    const cdp = await page.createCDPSession();
    await cdp.send('Page.setDownloadBehavior', {
      behavior: 'allow', downloadPath,
    });
  } catch (e) {
    console.log(`[INFO] Page.setDownloadBehavior indisponible : ${e.message}`);
  }
  try {
    const bcdp = await browser.target().createCDPSession();
    await bcdp.send('Browser.setDownloadBehavior', {
      behavior: 'allow', downloadPath,
    });
  } catch (e) { /* silencieux : doublon de sécurité */ }

  try {
    // ------------------------------------------------------------ ARRIVÉE
    // /manual redirige vers la chaîne OAuth (api.unity.com →
    // login.unity.com/en/conversations/...). On laisse la navigation
    // faire TOUTE la chaîne, puis on attend l'un des écrans connus.
    console.log('[INFO] Navigation vers license.unity3d.com/manual (chaîne OAuth en cascade) …');
    await page.goto('https://license.unity3d.com/manual',
      { waitUntil: 'domcontentloaded', timeout: 60000 });
    await sleep(5000);
    await shot(page, '01-arrivee.png');

    // ------------------------------------------------------------ LOGIN
    // Le portail actuel : 1er écran = EMAIL (« Next »), 2e écran =
    // MOT DE PASSE. Jamais les deux sur la même page désormais.
    async function completeLogin() {
      // --- Saisie de l'e-mail (avec plusieurs tentatives/écrans) ------
      for (let tour = 0; tour < 10; tour++) {
        const emailSel = await page.$(
          'input[type="email"], input[name="email"], input[id="user_email"],' +
          ' input[name="username"], input[id="Username"]');
        if (emailSel) {
          const val = await page.evaluate(
            (s) => (document.querySelector(s) || {}).value || '',
            'input[type="email"], input[name="email"], input[id="user_email"],' +
            ' input[name="username"], input[id="Username"]');
          if ((val || '').trim().includes('@') ||
            (val || '').trim().toLowerCase() === email.toLowerCase()) {
            // déjà rempli (itération précédente / cookie de session) :
            // on ne re-saisit pas, on valide juste
            const clicked = await clickReadyButton(page);
            await sleep(4000);
            if (!clicked && tour === 9) break;
            continue;
          }
          console.log('[INFO] Écran e-mail : saisie de l\'adresse …');
          // le portail login.unity.com est une app React : le champ doit
          // recevoir de VRAIS événements clavier (onChange), on ne peut
          // pas injecter la valeur en JS (sinon « Invalid email » au clic)
          await page.click('input[type="email"], input[name="email"],' +
            ' input[id="user_email"], input[name="username"], input[id="Username"]');
          await page.type('input[type="email"], input[name="email"],' +
            ' input[id="user_email"], input[name="username"], input[id="Username"]',
            email, { delay: 25 });
          await sleep(500);
          await shot(page, '03-identifiants-remplis.png');
          // le bouton peut être « Next », « Continue », ou input commit
          const clicked = await clickReadyButton(page);
          console.log(`[INFO] e-mail envoyé (${clicked || 'touche Entrée'})`);
          if (!clicked) {
            await page.keyboard.press('Enter').catch(() => {});
          }
          await sleep(4000);
          continue; // passe à l'écran suivant (souvent le mot de passe)
        }

        // --- Écran MOT DE PASSE ----------------------------------------
        if (await page.$('input[type="password"]')) {
          const visible = await page.evaluate(() => {
            const f = document.querySelector('input[type="password"]');
            if (!f) return false;
            const r = f.getBoundingClientRect();
            return r.width > 5 && r.height > 5 &&
              window.getComputedStyle(f).display !== 'none';
          });
          if (visible) {
            console.log('[INFO] Écran mot de passe : saisie …');
            await page.evaluate(() => {
              const f = document.querySelector('input[type="password"]');
              f.focus();
            });
            await page.type('input[type="password"]', password, { delay: 25 });
            await sleep(400);
            await shot(page, '03-identifiants-remplis.png');
            const clicked = await clickReadyButton(page);
            console.log(`[INFO] mot de passe envoyé (${clicked || 'Entrée'})`);
            if (!clicked) await page.keyboard.press('Enter').catch(() => {});
            await sleep(5000);
            continue;
          }
        }
        break; // plus d'écran de login reconnu : suite du flux
      }
    }

    // si on n'a JAMAIS vu le portail (cookie sst peut-être en vigueur) —
    // la boucle principale ci-dessous gère tout le reste.
    await completeLogin();

    // ------------------------------------------------------------ BOUCLE
    // Toute la suite est pilotée par ce que LA PAGE affiche : ToS, 2FA,
    // formulaire .alf, tuile Personal, capacité, boutons validés…
    const F_ALF = 'input[name="licenseFile"]';
    const F_TOS = 'button[name="conversations_accept_updated_tos_form[accept]"]';
    const F_TOTP = 'input[name="conversations_tfa_required_form[verify_code]"]';
    const F_EMAIL2FA = 'input[name="conversations_email_tfa_required_form[code]"]';

    let alfUploaded = false;
    let personalCliqué = false;
    let capaciteCliquée = false;
    let inaction = 0;
    for (let i = 0; i < 40; i++) {
      // --- ULF déjà téléchargé ? --------------------------------------
      const ulfFile = fs.readdirSync(OUT).find((f) => f.endsWith('.ulf'));
      if (ulfFile) {
        console.log(`[OK] ULF téléchargé : ${path.join(OUT, ulfFile)}`);
        await browser.close();
        return 0;
      }

      if (i % 4 === 0) await shot(page, `06-etape-${String(i).padStart(2, '0')}.png`);

      // --- 2FA ---------------------------------------------------------
      const kind2fa = (await page.$(F_TOTP)) ? 'totp'
        : ((await page.$(F_EMAIL2FA)) ? 'email' : null);
      if (kind2fa) {
        if (!authenticatorKey) {
          throw new Error('2FA demandée par Unity — fournir UNITY_TOTP_KEY dans Codemagic');
        }
        console.log('[INFO] Saisie du code 2FA (TOTP) …');
        await fillTwoFactor(page, 'totp', totp(authenticatorKey));
        await sleep(5000);
        continue;
      }

      // --- ToS (ancien portail serveur) --------------------------------
      if (await page.$(F_TOS)) {
        console.log('[INFO] Acceptation des nouvelles CGU …');
        await page.click(F_TOS).catch(() => {});
        await sleep(4000);
        continue;
      }

      // --- Formulaire d'UPLOAD du .alf ----------------------------------
      const alfInput = await page.$(F_ALF);
      if (alfInput && !alfUploaded) {
        console.log('[INFO] Envoi du fichier .alf …');
        await shot(page, '05-page-upload-alf.png');
        const alfInputHandle = await page.$(F_ALF);
        await alfInputHandle.uploadFile(alf);
        // Vue valide le CONTENU via FileReader (isValidated) : il faut
        // attendre que le bouton devienne actif avant de cliquer.
        let boutonPret = false;
        for (let t = 0; t < 20; t++) {
          boutonPret = await page.evaluate(() => {
            const btns = Array.from(document.querySelectorAll(
              'button, input[type="submit"]'));
            return btns.some((b) => !b.disabled &&
              (b.textContent || b.value || '').toLowerCase().match(
                /submit|valider|send|envoyer|next|suivant|continue|commencer/) &&
              b.getBoundingClientRect().width > 5);
          });
          if (boutonPret) break;
          await sleep(500);
        }
        if (!boutonPret) {
          console.log('[INFO] Bouton d\'upload pas encore actif, clic générique…');
          await clickReadyButton(page);
        }
        try {
          await page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 15000 });
        } catch (e) { /* ok : SPA */ }
        await sleep(3000);
        alfUploaded = true;
        continue;
      }

      // --- TUILE PERSONAL (étape 2, masquée par CSS) ---------------------
      if (!personalCliqué && alfUploaded) {
        const clique = await selectPersonal(page);
        if (clique) {
          console.log(`[INFO] Tuile Personal : (${clique})`);
          personalCliqué = true;
          await sleep(2500);
          continue;
        }
      }

      // --- CAPACITÉ (étape 2, après le choix du type) --------------------
      if (personalCliqué && !capaciteCliquée) {
        const cap = await selectCapacity(page);
        if (cap) {
          console.log('[INFO] Capacité Personal sélectionnée …');
          capaciteCliquée = true;
          await sleep(2000);
        }
      }

      // --- Bouton « next » de l'étape 2 puis confirmation finale --------
      if (alfUploaded) {
        const clique = await clickReadyButton(page);
        if (clique) {
          console.log(`[INFO] Bouton cliqué : « ${clique} »`);
          inaction = 0;
          try {
            await page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 8000 });
          } catch (e) { /* ok */ }
          await sleep(2500);
          continue;
        }
      }

      await sleep(3000);
      inaction++;
      if (inaction > 12) {
        throw new Error(
          'Flux d\'activation bloqué (aucune action possible côté page).');
      }
    }
    throw new Error('Aucun .ulf téléchargé — voir les captures 06-etape-*.png');
  } catch (err) {
    console.log(`[ERREUR] ${err && err.message ? err.message : err}`);
    try {
      await page.screenshot({ path: path.join(OUT, 'error.png'), fullPage: true });
      const html = await page.evaluate(
        () => document.documentElement.outerHTML);
      fs.writeFileSync(path.join(OUT, 'error.html'), html);
      console.log('[INFO] Captures de diagnostic : error.png + error.html');
    } catch (e) { /* tant pis */ }
    await browser.close().catch(() => {});
    process.exit(1);
  }
}

main();