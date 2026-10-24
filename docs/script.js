// Runeboard site: the nav that appears past the hero, reveal-on-scroll, the AI layer stack, and a
// lightbox for any picture marked data-zoom.
(function () {
  "use strict";

  // Navigation: shown once the hero scrolls away; highlights the section in view.
  var nav = document.getElementById("nav");
  var hero = document.querySelector(".hero");
  var links = Array.prototype.slice.call(document.querySelectorAll(".nav__links a"));
  function onScroll() {
    var past = hero.getBoundingClientRect().bottom < 80;
    nav.classList.toggle("is-visible", past);
    var current = null;
    links.forEach(function (a) {
      var target = document.querySelector(a.getAttribute("href"));
      if (target && target.getBoundingClientRect().top < window.innerHeight * 0.4) current = a;
    });
    links.forEach(function (a) { a.classList.toggle("is-active", a === current); });
  }
  window.addEventListener("scroll", onScroll, { passive: true });
  onScroll();

  // Reveal on scroll.
  var reveals = document.querySelectorAll(".reveal");
  if ("IntersectionObserver" in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        if (e.isIntersecting) { e.target.classList.add("is-in"); io.unobserve(e.target); }
      });
    }, { threshold: 0.12 });
    reveals.forEach(function (el) { io.observe(el); });
  } else {
    reveals.forEach(function (el) { el.classList.add("is-in"); });
  }

  // The AI stack: each layer swaps the explanation and its diagram.
  var layers = [
    { title: "Utility AI", img: "images/gameplay/33-utility-ai.webp",
      text: "Every nation constantly scores danger, opportunity, growth and stagnation — literally everything that can be quantified about the board — to weigh what is worth doing next." },
    { title: "Blackboards", img: "images/gameplay/31-blackboards.webp",
      text: "Each nation and each character keeps its own blackboard: what it has seen, the events it lived through, the scores it computed, and the missions it has taken on — memory that persists from turn to turn." },
    { title: "Hierarchical Task Network", img: "images/gameplay/30-htn.webp",
      text: "Missions are planned top-down: “hold the Gap of Rohan” breaks into marches, recruitments and actions that a specific character can actually carry out." },
    { title: "Behaviour & State Trees", img: "images/gameplay/32-blackboard.webp",
      text: "Characters execute their plans through behaviour and state trees, choosing among the feasible actions by their utility scores — and re-planning when the board changes under them." },
    { title: "Neural Network", img: "images/gameplay/29-mlp.webp",
      text: "A multilayer perceptron trained with supervised feedback on good and bad decisions gives every choice a second opinion, consulted alongside the rest of the stack." }
  ];
  var buttons = Array.prototype.slice.call(document.querySelectorAll(".ai__layer"));
  var fig = document.getElementById("ai-figure");
  var title = document.getElementById("ai-title");
  var text = document.getElementById("ai-text");
  function selectLayer(i) {
    buttons.forEach(function (b, j) {
      b.classList.toggle("is-active", i === j);
      b.setAttribute("aria-selected", i === j ? "true" : "false");
    });
    fig.src = layers[i].img;
    fig.alt = layers[i].title;
    title.textContent = layers[i].title;
    text.textContent = layers[i].text;
  }
  buttons.forEach(function (b, i) {
    b.addEventListener("click", function () { selectLayer(i); });
    b.addEventListener("keydown", function (e) {
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        var next = (i + (e.key === "ArrowDown" ? 1 : buttons.length - 1)) % buttons.length;
        buttons[next].focus();
        selectLayer(next);
      }
    });
  });

  // Lightbox.
  var box = document.getElementById("lightbox");
  var boxImg = box.querySelector("img");
  document.addEventListener("click", function (e) {
    var img = e.target.closest ? e.target.closest("img[data-zoom]") : null;
    if (img) {
      boxImg.src = img.currentSrc || img.src;
      boxImg.alt = img.alt;
      box.hidden = false;
      return;
    }
    if (!box.hidden && (e.target === box || e.target.classList.contains("lightbox__close") || e.target === boxImg)) box.hidden = true;
  });
  document.addEventListener("keydown", function (e) { if (e.key === "Escape") box.hidden = true; });
})();
