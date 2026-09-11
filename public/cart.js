// Lightweight localStorage cart. No framework — this store has ~4-8 products
// per category, doesn't need one. Subscription products (ranks) skip the
// cart entirely and go straight to their own checkout (see "Buy Now" below),
// since Stripe can't mix a subscription with one-time items in one session.

const CART_KEY = "apex_rust_cart";

function readCart() {
  try {
    return JSON.parse(localStorage.getItem(CART_KEY)) || [];
  } catch {
    return [];
  }
}

function writeCart(items) {
  localStorage.setItem(CART_KEY, JSON.stringify(items));
  updateCartBadge();
}

function addToCart(productId, name, priceCents, imageUrl) {
  const items = readCart();
  const existing = items.find((i) => i.productId === productId);
  if (existing) {
    existing.quantity += 1;
  } else {
    items.push({ productId, name, priceCents, imageUrl, quantity: 1 });
  }
  writeCart(items);
}

function removeFromCart(productId) {
  writeCart(readCart().filter((i) => i.productId !== productId));
}

function setQuantity(productId, quantity) {
  const items = readCart();
  const item = items.find((i) => i.productId === productId);
  if (!item) return;
  item.quantity = Math.max(1, quantity);
  writeCart(items);
}

function cartCount() {
  return readCart().reduce((sum, i) => sum + i.quantity, 0);
}

function updateCartBadge() {
  document.querySelectorAll("[data-cart-count]").forEach((el) => {
    el.textContent = cartCount();
  });
}

function updateCartDrawer() {
  const drawer = document.querySelector("[data-cart-drawer]");
  const itemsEl = document.querySelector("[data-cart-drawer-items]");
  const totalEl = document.querySelector("[data-cart-drawer-total]");
  if (!drawer || !itemsEl || !totalEl) return;
  const items = readCart();
  itemsEl.innerHTML = items.length
    ? items.map((item) => `<div class="cart-drawer-item"><span>${escapeHtml(item.name)} ×${item.quantity}</span><strong>${money(item.priceCents * item.quantity)}</strong></div>`).join("")
    : `<div class="cart-drawer-empty">Your cart is empty.</div>`;
  totalEl.textContent = money(items.reduce((sum, item) => sum + item.priceCents * item.quantity, 0));
}

function openCartDrawer() {
  const drawer = document.querySelector("[data-cart-drawer]");
  if (!drawer) return;
  drawer.classList.add("is-open");
  drawer.setAttribute("aria-hidden", "false");
  updateCartDrawer();
}

function closeCartDrawer() {
  const drawer = document.querySelector("[data-cart-drawer]");
  if (!drawer) return;
  drawer.classList.remove("is-open");
  drawer.setAttribute("aria-hidden", "true");
}

// Buttons with data-add-to-cart="productId|name|priceCents|imageUrl"
document.addEventListener("click", (e) => {
  const btn = e.target.closest("[data-add-to-cart]");
  if (!btn) return;
  const [productId, name, priceCents, imageUrl] = btn.dataset.addToCart.split("|");
  addToCart(productId, name, Number(priceCents), imageUrl);
  updateCartDrawer();
  openCartDrawer();
  const original = btn.textContent;
  btn.textContent = "Added ✓";
  setTimeout(() => (btn.textContent = original), 1200);
});

// Buttons with data-subscribe="productId" go straight to subscription checkout.
document.addEventListener("click", async (e) => {
  const btn = e.target.closest("[data-subscribe]");
  if (!btn) return;
  btn.disabled = true;
  btn.textContent = "Redirecting…";
  try {
    const res = await fetch("/api/checkout/subscription", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ productId: btn.dataset.subscribe }),
    });
    const data = await res.json();
    if (data.url) window.location.href = data.url;
    else throw new Error(data.error || "Checkout failed");
  } catch (err) {
    alert(err.message);
    btn.disabled = false;
    btn.textContent = "Subscribe";
  }
});

function money(cents) {
  return "£" + (cents / 100).toFixed(2);
}

let appliedGiftCard = null; // { code, balanceCents } once validated
let appliedDiscount = null; // { code, type, value } once validated — amount is recomputed live from current cart total

function computeDiscountAmount(discount, subtotalCents) {
  if (!discount) return 0;
  return discount.type === "percent"
    ? Math.round((subtotalCents * discount.value) / 100)
    : Math.min(discount.value, subtotalCents);
}

function escapeHtml(str) {
  return String(str ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function renderCartPage() {
  const container = document.getElementById("cart-container");
  if (!container) return;

  const items = readCart();
  if (items.length === 0) {
    container.innerHTML = `<div class="empty-state">Your cart is empty.<br><br><a class="btn" href="/kits">Browse Kits</a></div>`;
    return;
  }

  const rows = items
    .map(
      (i) => `
    <div class="cart-row" data-row="${escapeHtml(i.productId)}">
      <div class="thumb" style="background-image:url('${escapeHtml(i.imageUrl || "")}')"></div>
      <div class="name">${escapeHtml(i.name)}</div>
      <div class="qty">
        <button data-dec="${escapeHtml(i.productId)}">−</button>
        <span>${i.quantity}</span>
        <button data-inc="${escapeHtml(i.productId)}">+</button>
      </div>
      <div class="price">${money(i.priceCents * i.quantity)}</div>
      <button class="remove" data-remove="${escapeHtml(i.productId)}">Remove</button>
    </div>`
    )
    .join("");

  const total = items.reduce((sum, i) => sum + i.priceCents * i.quantity, 0);
  const discountAmount = computeDiscountAmount(appliedDiscount, total);
  const giftCardAmount = appliedGiftCard ? Math.min(appliedGiftCard.balanceCents, Math.max(0, total - discountAmount)) : 0;
  const remaining = total - discountAmount - giftCardAmount;

  container.innerHTML = `
    ${rows}
    <div class="gift-card-box" style="margin:16px 0; padding:14px; border:1px solid rgba(255,255,255,0.1); border-radius:8px;">
      <label>Discount code</label>
      <div style="display:flex; gap:8px;">
        <input type="text" id="discount-code-input" placeholder="e.g. WIPE10" style="flex:1; text-transform:uppercase;" value="${appliedDiscount ? appliedDiscount.code : ""}" ${appliedDiscount ? "disabled" : ""}>
        ${appliedDiscount
          ? `<button class="btn secondary" id="discount-remove-btn" type="button">Remove</button>`
          : `<button class="btn secondary" id="discount-apply-btn" type="button">Apply</button>`}
      </div>
      <div id="discount-code-status" class="muted" style="font-size:13px; margin-top:6px;">
        ${appliedDiscount ? `Applied — ${appliedDiscount.type === "percent" ? `${appliedDiscount.value}% off` : `${money(appliedDiscount.value)} off`}` : ""}
      </div>
    </div>
    <div class="gift-card-box" style="margin:16px 0; padding:14px; border:1px solid rgba(255,255,255,0.1); border-radius:8px;">
      <label>Gift card code</label>
      <div style="display:flex; gap:8px;">
        <input type="text" id="gift-code-input" placeholder="APEX-XXXX-XXXX-XXXX" style="flex:1;" value="${appliedGiftCard ? appliedGiftCard.code : ""}" ${appliedGiftCard ? "disabled" : ""}>
        ${appliedGiftCard
          ? `<button class="btn secondary" id="gift-remove-btn" type="button">Remove</button>`
          : `<button class="btn secondary" id="gift-apply-btn" type="button">Apply</button>`}
      </div>
      <div id="gift-code-status" class="muted" style="font-size:13px; margin-top:6px;">
        ${appliedGiftCard ? `Applied — ${money(appliedGiftCard.balanceCents)} available` : ""}
      </div>
    </div>
    <div class="cart-summary">
      <span>Subtotal</span>
      <span>${money(total)}</span>
    </div>
    ${discountAmount > 0 ? `
    <div class="cart-summary">
      <span>Discount (${escapeHtml(appliedDiscount.code)})</span>
      <span>−${money(discountAmount)}</span>
    </div>` : ""}
    ${giftCardAmount > 0 ? `
    <div class="cart-summary">
      <span>Gift card</span>
      <span>−${money(giftCardAmount)}</span>
    </div>` : ""}
    <div class="cart-summary">
      <span class="total">Total</span>
      <span class="total">${money(remaining)}</span>
    </div>
    ${remaining <= 0
      ? window.APEX_PLAYER_STEAMID
        ? `<div class="muted" style="margin:14px 0; font-size:13px;">Delivering to your linked Steam account (${window.APEX_PLAYER_STEAMID}).</div>`
        : `<div style="margin:14px 0;">
      <label>Your SteamID64 <span class="hint">— needed to deliver your kit(s), no Stripe step to collect it since the discount/gift card covers the full cost</span></label>
      <input type="text" id="gift-steamid-input" placeholder="17-digit SteamID64" pattern="[0-9]{17}" maxlength="17">
    </div>`
      : ""}
    <button class="btn block" id="checkout-btn">${remaining <= 0 ? "Redeem & Checkout" : "Checkout"}</button>
  `;

  container.querySelectorAll("[data-inc]").forEach((b) =>
    b.addEventListener("click", () => {
      const item = readCart().find((i) => i.productId === b.dataset.inc);
      setQuantity(b.dataset.inc, item.quantity + 1);
      renderCartPage();
    })
  );
  container.querySelectorAll("[data-dec]").forEach((b) =>
    b.addEventListener("click", () => {
      const item = readCart().find((i) => i.productId === b.dataset.dec);
      setQuantity(b.dataset.dec, item.quantity - 1);
      renderCartPage();
    })
  );
  container.querySelectorAll("[data-remove]").forEach((b) =>
    b.addEventListener("click", () => {
      removeFromCart(b.dataset.remove);
      renderCartPage();
    })
  );

  const discountApplyBtn = document.getElementById("discount-apply-btn");
  if (discountApplyBtn) {
    discountApplyBtn.addEventListener("click", async () => {
      const codeInput = document.getElementById("discount-code-input");
      const status = document.getElementById("discount-code-status");
      const code = codeInput.value.trim().toUpperCase();
      if (!code) return;
      discountApplyBtn.disabled = true;
      status.textContent = "Checking…";
      try {
        const res = await fetch("/api/discount/check", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ code, subtotalCents: total }),
        });
        const data = await res.json();
        if (data.valid) {
          appliedDiscount = { code, type: data.type, value: data.value };
          renderCartPage();
        } else {
          status.textContent = data.error || "Invalid discount code.";
          discountApplyBtn.disabled = false;
        }
      } catch {
        status.textContent = "Couldn't check that code — try again.";
        discountApplyBtn.disabled = false;
      }
    });
  }

  const discountRemoveBtn = document.getElementById("discount-remove-btn");
  if (discountRemoveBtn) {
    discountRemoveBtn.addEventListener("click", () => {
      appliedDiscount = null;
      renderCartPage();
    });
  }

  const applyBtn = document.getElementById("gift-apply-btn");
  if (applyBtn) {
    applyBtn.addEventListener("click", async () => {
      const codeInput = document.getElementById("gift-code-input");
      const status = document.getElementById("gift-code-status");
      const code = codeInput.value.trim().toUpperCase();
      if (!code) return;
      applyBtn.disabled = true;
      status.textContent = "Checking…";
      try {
        const res = await fetch("/api/gift-card/check", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ code }),
        });
        const data = await res.json();
        if (data.valid) {
          appliedGiftCard = { code, balanceCents: data.balanceCents };
          renderCartPage();
        } else {
          status.textContent = data.error || "Invalid or empty gift card code.";
          applyBtn.disabled = false;
        }
      } catch {
        status.textContent = "Couldn't check that code — try again.";
        applyBtn.disabled = false;
      }
    });
  }

  const removeBtn = document.getElementById("gift-remove-btn");
  if (removeBtn) {
    removeBtn.addEventListener("click", () => {
      appliedGiftCard = null;
      renderCartPage();
    });
  }

  document.getElementById("checkout-btn").addEventListener("click", async (e) => {
    const btn = e.target;
    const steamidInput = document.getElementById("gift-steamid-input");
    const steamid = window.APEX_PLAYER_STEAMID || (steamidInput ? steamidInput.value.trim() : undefined);
    if (steamidInput && !window.APEX_PLAYER_STEAMID && !/^[0-9]{17}$/.test(steamid)) {
      alert("Enter a valid 17-digit SteamID64.");
      return;
    }
    btn.disabled = true;
    btn.textContent = "Redirecting…";
    try {
      const res = await fetch("/api/checkout/payment", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          items: readCart().map((i) => ({ productId: i.productId, quantity: i.quantity })),
          giftCardCode: appliedGiftCard ? appliedGiftCard.code : undefined,
          discountCode: appliedDiscount ? appliedDiscount.code : undefined,
          steamid,
        }),
      });
      const data = await res.json();
      if (data.url) {
        localStorage.removeItem(CART_KEY); // Stripe will show its own confirmation
        window.location.href = data.url;
      } else {
        throw new Error(data.error || "Checkout failed");
      }
    } catch (err) {
      alert(err.message);
      btn.disabled = false;
      btn.textContent = remaining <= 0 ? "Redeem & Checkout" : "Checkout";
    }
  });
}

// Gift card purchase page (/gift-cards) — preset/custom amount, straight to
// its own Stripe checkout since it isn't part of the cart.
function initGiftCardPurchasePage() {
  const btn = document.getElementById("gift-checkout-btn");
  if (!btn) return;

  let selectedCents = null;
  document.querySelectorAll(".gift-amount").forEach((b) =>
    b.addEventListener("click", () => {
      selectedCents = Number(b.dataset.amount);
      document.getElementById("gift-amount-input").value = "";
      document.querySelectorAll(".gift-amount").forEach((x) => x.classList.remove("active"));
      b.classList.add("active");
    })
  );

  const customInput = document.getElementById("gift-amount-input");
  customInput.addEventListener("input", () => {
    selectedCents = null;
    document.querySelectorAll(".gift-amount").forEach((x) => x.classList.remove("active"));
  });

  btn.addEventListener("click", async () => {
    const customVal = parseFloat(customInput.value);
    const amountCents = selectedCents ?? (isNaN(customVal) ? null : Math.round(customVal * 100));
    if (!amountCents || amountCents < 500 || amountCents > 50000) {
      alert("Choose an amount between £5 and £500.");
      return;
    }
    btn.disabled = true;
    btn.textContent = "Redirecting…";
    try {
      const res = await fetch("/api/checkout/gift-card", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ amountCents }),
      });
      const data = await res.json();
      if (data.url) window.location.href = data.url;
      else throw new Error(data.error || "Checkout failed");
    } catch (err) {
      alert(err.message);
      btn.disabled = false;
      btn.textContent = "Buy Gift Card";
    }
  });
}

document.addEventListener("DOMContentLoaded", () => {
  updateCartBadge();
  updateCartDrawer();
  document.querySelector("[data-cart-close]")?.addEventListener("click", closeCartDrawer);
  renderCartPage();
  initGiftCardPurchasePage();
  initCategoryToolbar();
});

// Search + sort on category pages (/kits, /packages, /items, /ranks). Pure
// client-side — these catalogs are small (a handful to a few dozen items),
// so there's no need for a server round-trip on every keystroke or sort
// change. Cards carry their data (data-name, data-price-cents) already
// rendered server-side; this just filters/reorders the existing DOM nodes.
function initCategoryToolbar() {
  const toolbar = document.querySelector("[data-category-toolbar]");
  const grid = document.querySelector("[data-category-grid]");
  if (!toolbar || !grid) return;

  const searchInput = toolbar.querySelector("[data-category-search]");
  const sortSelect = toolbar.querySelector("[data-category-sort]");
  const noResults = document.querySelector("[data-category-no-results]");
  // Snapshot the server-rendered order once — "Sort: Featured" restores
  // this rather than needing a second round-trip to know the original order.
  const originalOrder = Array.from(grid.children);

  function apply() {
    const query = (searchInput.value || "").trim().toLowerCase();
    const sort = sortSelect.value;

    let cards = originalOrder.slice();
    if (sort === "price-asc") cards.sort((a, b) => Number(a.dataset.priceCents) - Number(b.dataset.priceCents));
    else if (sort === "price-desc") cards.sort((a, b) => Number(b.dataset.priceCents) - Number(a.dataset.priceCents));
    else if (sort === "name-asc") cards.sort((a, b) => a.dataset.name.localeCompare(b.dataset.name));

    let visibleCount = 0;
    cards.forEach((card) => {
      const matches = !query || card.dataset.name.includes(query);
      card.hidden = !matches;
      if (matches) visibleCount++;
      grid.appendChild(card); // re-append in sorted order — no-op for hidden cards, harmless
    });

    if (noResults) noResults.hidden = visibleCount > 0;
  }

  searchInput.addEventListener("input", apply);
  sortSelect.addEventListener("change", apply);
}
