// Browser E2E smoke for MentorPath (Blazor Interactive Server). Run against a running app with demo seed.
const { chromium } = require('playwright');

const BASE = process.env.BASE_URL || 'http://localhost:5237';
const PASSWORD = 'Demo-password-2026';
const SHOTS = process.env.SHOTS || require('path').join(__dirname, 'shots');
const fs = require('fs');
fs.mkdirSync(SHOTS, { recursive: true });

const stamp = Date.now().toString(36);
const menteeEmail = `e2e-${stamp}@mentorpath.local`;
let step = 0;

function log(msg) { console.log(`[${++step}] ${msg}`); }

async function settle(page) {
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(900); // allow the Blazor circuit to attach to prerendered markup
}

async function go(page, path) {
  await page.goto(BASE + path);
  await settle(page);
}

async function login(browser, email) {
  const ctx = await browser.newContext({ acceptDownloads: true });
  const page = await ctx.newPage();
  page.on('pageerror', e => console.log('  [pageerror]', e.message));
  await page.goto(BASE + '/Account/Login');
  await page.fill('[id="Input.Email"]', email);
  await page.fill('[id="Input.Password"]', PASSWORD);
  await Promise.all([page.waitForNavigation(), page.click('button[type=submit]')]);
  await settle(page);
  return page;
}

// Scores every rubric criterion (buttons 1-5) and fills the three feedback fields.
async function fillFeedback(page, strengths, improvements, challenge, level = 4) {
  const card = page.locator('.card:has-text("Feedback")').last();
  const groups = card.locator('[role=group]');
  const count = await groups.count();
  if (count === 0) throw new Error('No rubric criteria on the review form');
  for (let i = 0; i < count; i++) await groups.nth(i).locator(`button:text-is("${level}")`).click();
  await card.locator('label:has-text("Mocne strony") + textarea').fill(strengths);
  await card.locator('label:has-text("Do poprawy") + textarea').fill(improvements);
  await card.locator('label:has-text("Pytanie pogłębiające") + textarea').fill(challenge);
}

async function expectText(page, text, timeout = 10000) {
  await page.getByText(text, { exact: false }).first().waitFor({ timeout });
}

(async () => {
  const browser = await chromium.launch();
  let current;
  try {
    // Mentor A invites a new mentee.
    const mentor = current = await login(browser, 'mentor.a@mentorpath.local');
    await expectText(mentor, 'Dashboard');
    log('Mentor A logged in, dashboard visible');

    await go(mentor, '/mentor/mentees');
    await mentor.fill('#invite-email', menteeEmail);
    await mentor.click('text=Utwórz zaproszenie');
    const linkInput = mentor.locator('input.mono-break');
    await linkInput.waitFor({ timeout: 10000 });
    const inviteUrl = await linkInput.inputValue();
    log(`Invitation created: ${inviteUrl.replace(/invite\/.+$/, 'invite/<token>')}`);

    // New mentee accepts the invitation and completes onboarding.
    const menteeCtx = await browser.newContext({ acceptDownloads: true });
    const mentee = current = await menteeCtx.newPage();
    mentee.on('pageerror', e => console.log('  [pageerror]', e.message));
    await mentee.goto(inviteUrl.replace(/^https?:\/\/[^/]+/, BASE));
    await expectText(mentee, 'Zaproszenie do mentoringu');
    await mentee.fill('[id="Register.DisplayName"]', `Ewa ${stamp}`);
    await mentee.fill('[id="Register.Password"]', PASSWORD);
    await mentee.fill('[id="Register.ConfirmPassword"]', PASSWORD);
    await Promise.all([mentee.waitForNavigation(), mentee.click('text=Utwórz konto i dołącz')]);
    await settle(mentee);
    await expectText(mentee, 'Profil mentoringu');
    const areas = mentee.locator('textarea');
    await areas.nth(0).fill('Chcę projektować moduły backendu.');
    await areas.nth(1).fill('Granice modułów i spójność danych.');
    await mentee.locator('input[type=number]').fill('6');
    await mentee.click('button:has-text("Zapisz")');
    await expectText(mentee, 'Zapisano profil');
    log('Mentee registered via invitation and saved onboarding');

    // Reusing the invitation is rejected.
    const anon = await (await browser.newContext()).newPage();
    await anon.goto(inviteUrl.replace(/^https?:\/\/[^/]+/, BASE));
    await expectText(anon, 'Zaproszenie nieważne');
    log('Invitation token cannot be reused');

    // Mentor A creates a topic and an AI draft, then publishes it.
    current = mentor;
    await go(mentor, '/mentor/catalog/topics');
    await mentor.click('button:has-text("Nowy temat")');
    const topicTitle = `CQRS ${stamp}`;
    const titleInput = mentor.locator('.card-body input.form-control').first();
    await titleInput.fill(topicTitle);
    await titleInput.blur();
    await mentor.locator('.card-body input.form-control').nth(2).fill('Architektura');
    await mentor.locator('.card-body textarea').nth(0).fill('Kiedy CQRS ma sens.');
    await mentor.locator('.card-body textarea').nth(1).fill('- Rozpoznawanie kosztów CQRS');
    await mentor.click('button:has-text("Utwórz temat")');
    await mentor.waitForURL(/\/mentor\/catalog\/topics\/[0-9a-f-]+$/, { timeout: 10000 });
    await settle(mentor);
    log(`Topic "${topicTitle}" created`);

    await mentor.click('main a:has-text("Nowe ćwiczenie")');
    await mentor.waitForURL(/\/mentor\/exercises\/new/);
    await settle(mentor);
    await mentor.locator('textarea').nth(0).fill('Ocena, kiedy rozdzielić model odczytu i zapisu.');
    await mentor.click('button:has-text("Generate with AI")');
    await mentor.waitForURL(/\/mentor\/exercises\/[0-9a-f-]+\/edit$/, { timeout: 20000 });
    await settle(mentor);
    await expectText(mentor, 'AI Draft');
    log('AI draft generated and opened in editor (status Draft)');

    await mentor.locator('fieldset input.form-control').first().fill(`Decyzja CQRS ${stamp}`);
    await mentor.click('button:has-text("Opublikuj")');
    await expectText(mentor, 'Ćwiczenie opublikowane');
    log('Mentor edited and published the AI exercise');

    // Mentor A builds and publishes a roadmap for the new mentee.
    await go(mentor, '/mentor/mentees');
    await mentor.click(`a:has-text("Ewa ${stamp}")`);
    await mentor.waitForURL(/\/mentor\/mentees\/[0-9a-f-]+$/);
    await settle(mentor);
    await expectText(mentor, 'Chcę projektować moduły backendu.');
    await mentor.click('a:has-text("Roadmapa")');
    await mentor.waitForURL(/\/roadmap$/);
    await settle(mentor);
    await mentor.locator('textarea').fill('Świadome decyzje o modelu danych.');
    await mentor.click('button:has-text("Utwórz")');
    await expectText(mentor, 'Dodaj temat');
    const addTopic = async (title) => {
      const select = mentor.locator('.card:has-text("Dodaj temat") select');
      await select.selectOption({ label: title });
      await mentor.click('.card:has-text("Dodaj temat") button:has-text("Dodaj")');
      await mentor.locator(`.card-header:has-text("${title}")`).waitFor();
    };
    await addTopic(topicTitle);
    await addTopic('Spójność danych i transakcje');
    const assign = async (topic, exerciseLabelPart) => {
      const card = mentor.locator(`.card:has(.card-header:has-text("${topic}"))`);
      const select = card.locator('select');
      const value = await select.locator('option', { hasText: exerciseLabelPart }).first().getAttribute('value');
      await select.selectOption(value);
      await card.locator('button:has-text("Przypisz")').click();
      await card.locator('.list-row').first().waitFor();
    };
    await assign(topicTitle, `Decyzja CQRS ${stamp}`);
    await assign('Spójność danych i transakcje', 'Spójność zamówienia');
    await mentor.click('button:has-text("Opublikuj roadmapę")');
    await expectText(mentor, 'Roadmapa opublikowana');
    log('Roadmap with 2 topics published');

    // Mentee solves the quiz (wrong, then right) without seeing answers upfront.
    current = mentee;
    await go(mentee, '/app');
    await expectText(mentee, 'Następne zadania');
    await mentee.click('a:has-text("Spójność zamówienia")');
    await mentee.waitForURL(/\/app\/assignments\//);
    await settle(mentee);
    const quizUrl = mentee.url();
    const html = await mentee.content();
    if (html.includes('Sprawdź, czy mentee rozumie') || html.includes('jest uzasadnione dopiero')) throw new Error('Mentor guide or explanation leaked to mentee');
    await mentee.click('label:has-text("Saga z kompensacjami")');
    await mentee.locator('textarea').fill('Saga daje niezależność modułów.');
    await mentee.click('label[for$="-3"]');
    await mentee.click('button:has-text("Wyślij")');
    await expectText(mentee, 'To nie jest najlepsza odpowiedź');
    await mentee.click('label:has-text("Jedna transakcja bazodanowa")');
    await mentee.locator('textarea').fill('Jedna baza i jeden proces — transakcja wystarczy.');
    await mentee.click('label[for$="-4"]');
    await mentee.click('button:has-text("Wyślij")');
    await expectText(mentee, 'Poprawna odpowiedź');
    await expectText(mentee, 'To zadanie jest zaliczone');
    log('Quiz: incorrect then correct attempt, explanation shown only after correct answer');

    // Mentee submits the architectural decision.
    await go(mentee, '/app');
    await mentee.click(`a:has-text("Decyzja CQRS ${stamp}")`);
    await mentee.waitForURL(/\/app\/assignments\//);
    await settle(mentee);
    const decisionUrl = mentee.url();
    const fields = mentee.locator('textarea');
    const answers = ['Bez CQRS na start', 'Mały ruch i zespół', 'Pełny CQRS, read replica', 'Prostota kosztem skalowania odczytów', 'Wolne raporty', 'Gdy odczyty > 10x zapisów'];
    for (let i = 0; i < 6; i++) await fields.nth(i).fill(answers[i]);
    await mentee.click('label[for$="-3"]');
    await mentee.click('button:has-text("Wyślij")');
    await expectText(mentee, 'czeka na ocenę mentora');
    log('ADR submitted (PendingReview)');

    // Mentor requests revision.
    current = mentor;
    await go(mentor, '/mentor/reviews');
    await mentor.click(`a:has-text("Decyzja CQRS ${stamp}")`);
    await mentor.waitForURL(/\/mentor\/reviews\/[0-9a-f-]+$/);
    await settle(mentor);
    await expectText(mentor, 'Mentor guide');
    await fillFeedback(mentor, 'Dobra analiza alternatyw.', 'Brakuje opisu ryzyk operacyjnych.', 'Co się stanie przy 10x ruchu?', 3);
    await mentor.click('button:has-text("Needs revision")');
    await expectText(mentor, 'została już oceniona');
    log('Mentor sent the ADR back for revision');

    // Mentee reads feedback, writes reflection and resubmits.
    current = mentee;
    await go(mentee, new URL(decisionUrl).pathname + '/history');
    await expectText(mentee, 'Brakuje opisu ryzyk operacyjnych.');
    await expectText(mentee, 'Ocena procesu decyzyjnego');
    await expectText(mentee, '3/5');
    await mentee.click('.card:has-text("Refleksja po ocenie") label[for$="-3"]');
    await mentee.locator('.card:has-text("Refleksja po ocenie") textarea').fill('Muszę opisywać ryzyka.');
    await mentee.click('button:has-text("Zapisz refleksję")');
    await expectText(mentee, 'Muszę opisywać ryzyka.');
    log('Reflection saved for the reviewed attempt');

    await go(mentee, new URL(decisionUrl).pathname);
    await expectText(mentee, 'próba #2');
    const f2 = mentee.locator('textarea');
    for (let i = 0; i < 6; i++) await f2.nth(i).fill(answers[i] + ' (poprawione)');
    await mentee.click('label[for$="-4"]');
    await mentee.click('button:has-text("Wyślij")');
    await expectText(mentee, 'Próba #2 wysłana');
    log('Mentee submitted attempt #2');

    // Mentor accepts.
    current = mentor;
    await go(mentor, '/mentor/reviews');
    await mentor.click(`a:has-text("Decyzja CQRS ${stamp}")`);
    await mentor.waitForURL(/\/mentor\/reviews\/[0-9a-f-]+$/);
    await settle(mentor);
    await fillFeedback(mentor, 'Kompletne.', 'Drobne poprawki.', 'Kiedy wrócisz do decyzji?', 5);
    await mentor.click('button:has-text("Accepted")');
    await expectText(mentor, 'została już oceniona');
    log('Mentor accepted attempt #2');

    // Progress is 100%.
    current = mentee;
    await go(mentee, '/app/mentorships');
    await mentee.click('a:has-text("Postęp")');
    await mentee.waitForURL(/\/app\/progress\//);
    await settle(mentee);
    await expectText(mentee, '100%');
    log('Mentee progress shows 100% (2/2 topics)');

    // Practical upload via UI by demo mentee Ola (after mentor review of her pending attempt, if any).
    const ola = current = await login(browser, 'ola@mentorpath.local');
    await go(ola, '/app');
    const practicalLink = ola.locator('a:has-text("Idempotentny endpoint płatności")').first();
    if (await practicalLink.count() > 0) {
      await practicalLink.click();
      await ola.waitForURL(/\/app\/assignments\//);
      await settle(ola);
      if (await ola.getByText('czeka na ocenę mentora').count() > 0) {
        current = mentor;
        await go(mentor, '/mentor/reviews');
        await mentor.click('a:has-text("Idempotentny endpoint płatności")');
        await settle(mentor);
        await fillFeedback(mentor, 'OK', 'Dodaj testy', 'Retencja kluczy?');
        await mentor.click('button:has-text("Needs revision")');
        await expectText(mentor, 'została już oceniona');
        current = ola;
        await ola.reload(); await settle(ola);
      }
      fs.writeFileSync(require('path').join(SHOTS, 'adr.pdf'), '%PDF-1.4\n% e2e\n');
      await ola.locator('input[type=url]').fill('https://github.com/ola/payments');
      await ola.setInputFiles('input[type=file]', require('path').join(SHOTS, 'adr.pdf'));
      await ola.locator('textarea').fill('Unikalny indeks + klucz idempotencji.');
      await ola.click('label[for$="-4"]');
      await ola.click('button:has-text("Wyślij")');
      await expectText(ola, 'wysłana — czeka na ocenę mentora');
      await go(ola, new URL(ola.url()).pathname + '/history');
      const [download] = await Promise.all([ola.waitForEvent('download'), ola.click('a:has-text("adr.pdf")')]);
      const p = await download.path();
      if (!fs.readFileSync(p, 'utf8').startsWith('%PDF')) throw new Error('Downloaded attachment content mismatch');
      log('Practical task: link + PDF uploaded through the UI and downloaded by the author');
    }

    // Mentor B cannot open Mentor A resources through the UI.
    const mentorB = current = await login(browser, 'mentor.b@mentorpath.local');
    const reviewUrl = mentor.url();
    await go(mentorB, new URL(reviewUrl).pathname);
    await expectText(mentorB, 'Nie znaleziono');
    await go(mentorB, '/mentor/mentees');
    if ((await mentorB.content()).includes(`Ewa ${stamp}`)) throw new Error('Mentor B sees Mentor A mentee');
    log('Mentor B cannot see Mentor A review or mentees');

    console.log('E2E OK');
    await browser.close();
  } catch (err) {
    console.error('E2E FAILED at step', step + 1, err.message);
    if (current) await current.screenshot({ path: `${SHOTS}/failure.png`, fullPage: true }).catch(() => {});
    await browser.close();
    process.exit(1);
  }
})();
