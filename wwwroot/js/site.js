// Progressive enhancements. Every page works without this script; it adds live previews and conveniences.
(function () {
  'use strict';

  const NB = ' ';
  const MON = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const $ = (sel, root) => (root || document).querySelector(sel);
  const $$ = (sel, root) => Array.from((root || document).querySelectorAll(sel));

  // Same output as Format.Zar on the server: R 12 500.00
  function zar(n) {
    const v = Math.abs(n).toFixed(2).split('.');
    return (n < 0 ? '−' : '') + 'R' + NB + v[0].replace(/\B(?=(\d{3})+(?!\d))/g, NB) + '.' + v[1];
  }
  const pl = (n, w) => n + ' ' + w + (n === 1 ? '' : 's');
  const r2 = (n) => Math.round(n * 100) / 100;
  const num = (el) => parseFloat(el && el.value) || 0;

  // Same as DateOnly.AddMonths: keeps the day, clamped to the end of shorter months.
  function addMonths(iso, n) {
    const [y, m, d] = iso.split('-').map(Number);
    const t = new Date(Date.UTC(y, m - 1 + n, 1));
    const last = new Date(Date.UTC(t.getUTCFullYear(), t.getUTCMonth() + 1, 0)).getUTCDate();
    t.setUTCDate(Math.min(d, last));
    return t;
  }
  const fmtDate = (t) => t.getUTCDate() + ' ' + MON[t.getUTCMonth()] + ' ' + t.getUTCFullYear();

  // ---------- login: demo account buttons fill the phone number ----------
  $$('[data-demo-phone]').forEach((btn) => {
    btn.addEventListener('click', () => {
      $$('[data-demo-phone]').forEach((b) => b.setAttribute('aria-pressed', String(b === btn)));
      const phone = $('#Phone');
      if (phone) phone.value = btn.dataset.demoPhone;
      const pin = $('#Pin');
      if (pin) pin.focus();
    });
  });

  // ---------- auto-submitting filters ----------
  $$('[data-autosubmit]').forEach((el) => {
    let timer;
    const submit = () => el.form && el.form.requestSubmit();
    el.addEventListener(el.tagName === 'SELECT' ? 'change' : 'input', () => {
      clearTimeout(timer);
      timer = setTimeout(submit, el.tagName === 'SELECT' ? 0 : 400);
    });
  });
  // Keep the cursor in the search box after it reloads the page.
  const search = $('input[type=search][data-autosubmit]');
  if (search && search.value) { search.focus(); search.setSelectionRange(search.value.length, search.value.length); }

  // ---------- confirmation dialogs ----------
  // Focus moves into the dialog, Tab stays inside it, Escape closes it, and focus goes back to the form behind it.
  $$('[data-dialog]').forEach((overlay) => {
    const dialog = $('[role=dialog]', overlay) || overlay;
    const form = overlay.closest('form');
    const focusable = () => $$('a[href], button:not([disabled]), input:not([disabled]):not([type=hidden]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])', dialog)
      .filter((el) => el.offsetParent !== null);
    const onKey = (e) => {
      if (e.key === 'Escape') { close(); return; }
      if (e.key !== 'Tab') return;
      const items = focusable();
      if (!items.length) { e.preventDefault(); return; }
      const first = items[0], last = items[items.length - 1];
      const outside = !dialog.contains(document.activeElement);
      if (e.shiftKey && (outside || document.activeElement === first)) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && (outside || document.activeElement === last)) { e.preventDefault(); first.focus(); }
    };
    function close() {
      document.removeEventListener('keydown', onKey);
      overlay.remove();
      const back = form && $$('input:not([type=hidden]), select, textarea', form).find((el) => el.offsetParent !== null);
      if (back) back.focus();
    }
    document.addEventListener('keydown', onKey);
    $$('[data-close-dialog]', overlay).forEach((b) => b.addEventListener('click', (e) => { e.preventDefault(); close(); }));
    overlay.addEventListener('mousedown', (e) => { if (e.target === overlay) close(); });
    const start = $('[autofocus]', dialog) || focusable()[0];
    if (start) start.focus();
  });

  // ---------- error messages: tie each one to its field ----------
  // The server prints the message next to the field. This lets a screen reader read it with the field,
  // and puts the keyboard on the first field that needs fixing.
  $$('[data-valmsg-for]').forEach((msg) => {
    if (!msg.textContent.trim()) return;
    const field = document.getElementsByName(msg.getAttribute('data-valmsg-for'))[0];
    if (!field) return;
    if (!msg.id) msg.id = 'err-' + msg.getAttribute('data-valmsg-for').replace(/[^\w-]/g, '_');
    const ids = (field.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
    if (!ids.includes(msg.id)) ids.push(msg.id);
    field.setAttribute('aria-describedby', ids.join(' '));
    field.setAttribute('aria-invalid', 'true');
  });
  $$('.validation-summary-errors').forEach((el) => el.setAttribute('role', 'alert'));
  const firstInvalid = $('[aria-invalid="true"]');
  if (firstInvalid && document.activeElement === document.body) firstInvalid.focus();

  // ---------- fields that select their text when tapped (links to copy) ----------
  $$('[data-select]').forEach((el) => el.addEventListener('focus', () => el.select()));

  // ---------- one tap, one submission ----------
  // On a slow connection people tap twice. The buttons switch off after the first tap. The server also refuses
  // an identical payment that is still waiting, in case the page is submitted twice some other way.
  $$('form').filter((f) => f.method === 'post').forEach((form) => {
    form.addEventListener('submit', (e) => {
      if (form.dataset.submitting === 'true') { e.preventDefault(); return; }
      form.dataset.submitting = 'true';
      // Switch off after the browser has read the form: a disabled button's own value is not sent.
      setTimeout(() => {
        $$('button[type=submit], button:not([type]), input[type=submit]', form).forEach((b) => { b.disabled = true; b.setAttribute('aria-busy', 'true'); });
      }, 0);
    });
  });
  // Coming back with the browser's Back button must not leave a form switched off.
  window.addEventListener('pageshow', (e) => {
    if (!e.persisted) return;
    $$('form[data-submitting]').forEach((form) => {
      delete form.dataset.submitting;
      $$('button[aria-busy]', form).forEach((b) => { b.disabled = false; b.removeAttribute('aria-busy'); });
    });
  });

  // ---------- contact picker (only where the browser offers one) ----------
  if (navigator.contacts && navigator.contacts.select) {
    $$('[data-contact-picker], [data-pick-contact]').forEach((el) => el.removeAttribute('hidden'));
    $$('[data-pick-contact]').forEach((btn) => {
      btn.addEventListener('click', async () => {
        try {
          const [c] = await navigator.contacts.select(['name', 'tel'], { multiple: false });
          if (!c) return;
          const name = $(btn.dataset.name), phone = $(btn.dataset.phone);
          if (name && c.name && c.name[0]) name.value = c.name[0];
          if (phone && c.tel && c.tel[0]) phone.value = c.tel[0];
        } catch (_) { /* picker closed */ }
      });
    });
  }

  // ---------- new loan: live total and schedule ----------
  const loanForm = $('[data-loan-form]');
  if (loanForm) {
    const principal = $('#Principal', loanForm), rate = $('#RatePercent', loanForm);
    const start = $('#StartDate', loanForm), term = $('#Term', loanForm);
    const update = () => {
      const p = num(principal), r = num(rate), t = parseInt(term.value, 10) || 1;
      const type = ($('input[name=Type]:checked', loanForm) || {}).value;
      const interest = type === 'Monthly' ? p * r / 100 * t : p * r / 100;
      const total = p + interest;
      const inst = r2(total / t);
      $('[data-total]', loanForm).textContent = zar(total);
      $('[data-breakdown]', loanForm).textContent = zar(p) + ' lent + ' + zar(interest) + ' interest';
      $('[data-interest-help]', loanForm).textContent = type === 'Monthly'
        ? r + '% of the amount lent is added every month, for ' + pl(t, 'month') + ' (' + r2(r * t) + '% in total).'
        : 'One flat charge of ' + r + '% on the amount lent, added when the loan starts.';
      const sched = $('[data-schedule]', loanForm);
      $$('.sched__row, .sched__more', sched).forEach((el) => el.remove());
      for (let i = 1; i <= Math.min(4, t); i++) {
        const row = document.createElement('div');
        row.className = 'sched__row';
        row.innerHTML = '<span class="sched__n"><span></span></span><span style="font-weight:700"></span>';
        row.querySelector('.sched__n > span').textContent = i;
        row.querySelector('.sched__n').append(start.value ? fmtDate(addMonths(start.value, i)) : '');
        row.lastElementChild.textContent = zar(inst);
        sched.append(row);
      }
      if (t > 4) {
        const more = document.createElement('p');
        more.className = 'sched__more';
        more.textContent = '+ ' + pl(t - 4, 'more payment') + ' of ' + zar(inst) + ' each';
        sched.append(more);
      }
    };
    loanForm.addEventListener('input', update);
    loanForm.addEventListener('change', update);
  }

  // ---------- repayment: live new balance ----------
  const repayForm = $('[data-repay-form]');
  if (repayForm) {
    const balance = parseFloat(repayForm.dataset.balance) || 0;
    const amount = $('#Amount', repayForm);
    const err = $('[data-repay-error]', repayForm);
    const update = () => {
      const a = num(amount);
      const msg = a <= 0 ? 'Enter the amount you received.' : a > balance + 0.001 ? 'That is more than the ' + zar(balance) + ' still owing.' : '';
      const nb = msg ? balance : Math.max(0, r2(balance - a));
      err.textContent = msg;
      err.hidden = !msg;
      amount.closest('.affix').classList.toggle('affix--error', !!msg);
      $('[data-this-payment]', repayForm).textContent = '− ' + zar(msg ? 0 : a);
      $('[data-new-balance]', repayForm).textContent = zar(nb);
      $('[data-new-note]', repayForm).textContent = msg ? 'Fix the amount to see the new balance.' : nb === 0 ? 'This pays the loan off in full.' : 'Still owing after this payment.';
    };
    amount.addEventListener('input', update);
    $$('[data-fill-amount]', repayForm).forEach((b) => b.addEventListener('click', () => { amount.value = b.dataset.fillAmount; update(); }));
  }

  // ---------- printable report ----------
  $$('[data-print]').forEach((b) => b.addEventListener('click', () => window.print()));
})();
