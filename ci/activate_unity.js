#!/usr/bin/env node
// =====================================================================
//  activate_unity.js — activation de licence Unity PERSONAL automatisée
//  pour le CI (Codemagic), sans intervention humaine.
//
//  Adapté de game-ci/unity-license-activate (logique 2020), avec :
//   - login par sélecteurs GÉNÉRIQUES (le site id.unity.com a changé
//     d'apparence : plus de #conversations_create_session_form_...) ;
//   - une capture d'écran numérotée à CHAQUE étape (01-...png) copiée
//     dans les artefacts Codemagic le cas échéant — si Unity change
//     une page, la capture montre exactement quoi corriger ;
//   - HTML de débogage en cas d'échec (error.html).
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

async function main() {
  const [email, password, alf, authenticatorKey] = process.argv.slice(2);
  if (!email || !password || !alf) {
    console.log('[ERREUR] usage : node activate_unity.js EMAIL MOT_DE_PASSE fichier.alf [cleTOTP]');
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
    await cdp.send('Page.setDownloadBehavior',
      { behavior: 'allow', downloadPath });
  } catch (e) {
    console.log(`[INFO] Page.setDownloadBehavior indisponible : ${e.message}`);
  }
  try {
    const bcdp = await browser.target().createCDPSession();
    await bcdp.send('Browser.setDownloadBehavior',
      { behavior: 'allow', downloadPath });
  } catch (e) { /* silencieux : doublon de sécurité */ }

  try {
    // ------------------------------------------------------------ REDIRECTION
    console.log('[INFO] Navigation vers license.unity3d.com/manual …');
    await page.goto('https://license.unity3d.com/manual',
      { waitUntil: 'domcontentloaded', timeout: 60000 });
    await page.waitForNavigation({ waitUntil: 'load', timeout: 45000 })
      .catch(() => {});
    await sleep(3000);
    await shot(page, '01-arrivee.png');

    // ------------------------------------------------------------ LOGIN
    console.log('[INFO] Attente du formulaire de connexion (sélecteurs génériques) …');
    const pwdField = await waitForFirst(page,
      ['input[type="password"]'], 60000);
    if (!pwdField) {
      throw new Error('Aucun champ mot de passe trouvé sur la page de connexion');
    }
    const emailField = await waitForFirst(page,
      ['input[type="email"]', 'input[name="email"]', 'input[name="user[email]"]'],
      15000);
    if (!emailField) {
      // certains portails demandent un choix "email / Google" d'abord
      const mailLink = await waitForFirst(page,
        ['a[data-event="toMailLogin"]', 'a[rel="nofollow"]'], 10000);
      if (mailLink) {
        console.log('[INFO] Ouverture de la connexion par e-mail …');
        await page.click(mailLink).catch(() => {});
        await sleep(4000);
      }
    }
    await shot(page, '02-avant-saisie.png');

    const sel = await waitForFirst(page,
      ['input[type="email"]', 'input[name="email"]', 'input[name="user[email]"]',
        'input[id="user_email"]'], 20000);
    if (sel) {
      await page.click(sel);
      await page.type(sel, email, { delay: 25 });
    } else {
      throw new Error('Aucun champ e-mail trouvé');
    }
    await page.click('input[type="password"]');
    await page.type('input[type="password"]', password, { delay: 25 });
    await shot(page, '03-identifiants-remplis.png');

    await submit(page);
    try {
      await page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 30000 });
    } catch (e) { /* les SPA n'ont pas toujours de navigation */ }
    await sleep(4000);
    await shot(page, '04-apres-login.png');

    // ------------------------------------------------------------ BOUCLE
    // états possibles après login : ToS, 2FA, formulaire .alf, erreur
    const F_ALF = 'input[name="licenseFile"]';
    const F_TOS = 'button[name="conversations_accept_updated_tos_form[accept]"]';
    const F_TOTP = 'input[name="conversations_tfa_required_form[verify_code]"]';
    const F_EMAIL2FA = 'input[name="conversations_email_tfa_required_form[code]"]';

    let reached = false;
    for (let i = 0; i < 10; i++) {
      if (await page.$(F_ALF)) { reached = true; break; }

      console.log(`[INFO] État après login, tour ${i + 1}/10 …`);
      if (await page.$(F_TOS)) {
        console.log('[INFO] Acceptation des nouvelles CGU …');
        await page.click(F_TOS).catch(() => {});
        await sleep(4000);
        continue;
      }
      if (await page.$(F_TOTP) || await page.$(F_EMAIL2FA)) {
        if (!authenticatorKey) {
          throw new Error(
            '2FA demandée par Unity — fournir UNITY_TOTP_KEY dans Codemagic');
        }
        console.log('[INFO] Saisie du code 2FA (TOTP) …');
        const code = totp(authenticatorKey);
        const field = (await page.$(F_TOTP)) ? F_TOTP : F_EMAIL2FA;
        await page.click(field);
        await page.type(field, code, { delay: 30 });
        await submit(page);
        await sleep(5000);
        continue;
      }
      if (await page.$('input[type="password"]')) {
        // re-présenté : identifiants refusés ?
        throw new Error('Le formulaire de login est réapparu — identifiants refusés ?');
      }
      await sleep(3000);
    }
    if (!reached) throw new Error('Formulaire .alf introuvable après login');
    await shot(page, '05-page-upload-alf.png');

    // ------------------------------------------------------------ ENVOI .ALF
    console.log('[INFO] Envoi du fichier .alf …');
    const input = await page.$(F_ALF);
    await input.uploadFile(alf);
    await submit(page);
    try {
      await page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 30000 });
    } catch (e) { /* ok */ }
    await sleep(3000);

    // ------------------------------------------------------------ PERSONAL
    console.log('[INFO] Choix du type de licence (Personal) …');
    await shot(page, '06-choix-type.png');
    const tilePersonal = await waitForFirst(page,
      ['input[id="type_personal"]', 'input[value="personal"]'], 40000);
    if (!tilePersonal) throw new Error('Tuile Personal introuvable sur la page de choix');
    await page.evaluate(
      (s) => document.querySelector(s).click(), tilePersonal);

    const cap = await waitForFirst(page,
      ['input[id="option3"]', 'input[name="personal_capacity"]'], 20000);
    if (cap) {
      await page.evaluate(
        (s) => document.querySelector(s).click(), cap);
    }
    const next = await waitForFirst(page,
      ['input[class="btn mb10"]', 'button[type="submit"]'], 10000);
    if (next) {
      await page.evaluate(
        (s) => document.querySelector(s).click(), next);
    }
    await sleep(2000);
    const confirm = await waitForFirst(page,
      ['input[name="commit"]', 'button[type="submit"]'], 10000);
    if (confirm) {
      await submit(page);
      await sleep(2000);
      await submit(page).catch(() => {}); // page des conditions Personal
    }

    // ------------------------------------------------------------ ULF
    console.log(`[INFO] Attente du téléchargement du .ulf dans ${OUT} …`);
    for (let i = 0; i < 120; i++) {
      const ulfFile = fs.readdirSync(OUT)
        .find((f) => f.endsWith('.ulf'));
      if (ulfFile) {
        console.log(`[OK] ULF téléchargé : ${path.join(OUT, ulfFile)}`);
        await browser.close();
        return 0;
      }
      await sleep(1000);
    }
    throw new Error('Aucun .ulf téléchargé après 2 minutes');
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