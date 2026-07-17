// Liquid Glass Effect for Pill Elements
// Based on liquid-glass by Shu Ding (https://github.com/shuding/liquid-glass)
// Adapted to apply to all .pill elements in OML Launcher

(function() {
  'use strict';

  var filterId = 'oml-liquid-glass-filter';
  var canvasDPI = 2;
  var mapWidth = 400;
  var mapHeight = 100;
  var created = false;

  function smoothStep(a, b, t) {
    t = Math.max(0, Math.min(1, (t - a) / (b - a)));
    return t * t * (3 - 2 * t);
  }

  function length(x, y) {
    return Math.sqrt(x * x + y * y);
  }

  function roundedRectSDF(x, y, width, height, radius) {
    var qx = Math.abs(x) - width + radius;
    var qy = Math.abs(y) - height + radius;
    return Math.min(Math.max(qx, qy), 0) + length(Math.max(qx, 0), Math.max(qy, 0)) - radius;
  }

  function generateDisplacementMap() {
    var w = mapWidth * canvasDPI;
    var h = mapHeight * canvasDPI;
    var canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    var ctx = canvas.getContext('2d');
    var data = ctx.createImageData(w, h);

    // Generate rounded-rect displacement pattern
    var maxScale = 0;
    var rawValues = [];

    for (var i = 0; i < data.data.length; i += 4) {
      var x = (i / 4) % w;
      var y = Math.floor(i / 4 / w);
      var uvx = x / w - 0.5;
      var uvy = y / h - 0.5;

      var distanceToEdge = roundedRectSDF(uvx, uvy, 0.4, 0.4, 0.3);
      var displacement = smoothStep(0.6, 0, distanceToEdge - 0.08);
      var scaled = smoothStep(0, 1, displacement);

      var tx = uvx * scaled + 0.5;
      var ty = uvy * scaled + 0.5;
      var dx = tx * w - x;
      var dy = ty * h - y;
      maxScale = Math.max(maxScale, Math.abs(dx), Math.abs(dy));
      rawValues.push(dx, dy);
    }

    maxScale = maxScale * 0.5 + 1;

    var idx = 0;
    for (var j = 0; j < data.data.length; j += 4) {
      var r = rawValues[idx++] / maxScale + 0.5;
      var g = rawValues[idx++] / maxScale + 0.5;
      data.data[j] = r * 255;
      data.data[j + 1] = g * 255;
      data.data[j + 2] = 128;
      data.data[j + 3] = 255;
    }

    ctx.putImageData(data, 0, 0);
    return { url: canvas.toDataURL(), scale: maxScale / canvasDPI };
  }

  function createSVGFilter() {
    if (created) return;
    created = true;

    var map = generateDisplacementMap();

    var svgNS = 'http://www.w3.org/2000/svg';
    var svg = document.createElementNS(svgNS, 'svg');
    svg.setAttribute('xmlns', svgNS);
    svg.setAttribute('width', '0');
    svg.setAttribute('height', '0');
    svg.style.cssText = 'position: fixed; top: 0; left: 0; pointer-events: none; z-index: -1;';

    var defs = document.createElementNS(svgNS, 'defs');

    var filter = document.createElementNS(svgNS, 'filter');
    filter.setAttribute('id', filterId);
    filter.setAttribute('filterUnits', 'userSpaceOnUse');
    filter.setAttribute('colorInterpolationFilters', 'sRGB');
    filter.setAttribute('x', '-10%');
    filter.setAttribute('y', '-10%');
    filter.setAttribute('width', '120%');
    filter.setAttribute('height', '120%');

    var feImage = document.createElementNS(svgNS, 'feImage');
    feImage.setAttribute('result', 'map');
    feImage.setAttributeNS('http://www.w3.org/1999/xlink', 'xlink:href', map.url);
    feImage.setAttribute('x', '0');
    feImage.setAttribute('y', '0');
    feImage.setAttribute('width', mapWidth.toString());
    feImage.setAttribute('height', mapHeight.toString());
    feImage.setAttribute('preserveAspectRatio', 'none');

    var feDisplacementMap = document.createElementNS(svgNS, 'feDisplacementMap');
    feDisplacementMap.setAttribute('in', 'SourceGraphic');
    feDisplacementMap.setAttribute('in2', 'map');
    feDisplacementMap.setAttribute('xChannelSelector', 'R');
    feDisplacementMap.setAttribute('yChannelSelector', 'G');
    feDisplacementMap.setAttribute('scale', map.scale.toString());

    filter.appendChild(feImage);
    filter.appendChild(feDisplacementMap);
    defs.appendChild(filter);
    svg.appendChild(defs);
    document.body.appendChild(svg);

    // Apply styles to pill elements
    var pills = document.querySelectorAll('.pill');
    var glassStyle = document.createElement('style');
    glassStyle.textContent =
      '.pill.liquid-glass-ready { ' +
      'backdrop-filter: url(#' + filterId + ') blur(2px) saturate(1.5) contrast(1.08) brightness(1.05);' +
      '-webkit-backdrop-filter: url(#' + filterId + ') blur(2px) saturate(1.5) contrast(1.08) brightness(1.05);' +
      '}';
    document.head.appendChild(glassStyle);

    pills.forEach(function(pill) {
      pill.classList.add('liquid-glass-ready');
    });

    // Also watch for new pills
    if (typeof MutationObserver !== 'undefined') {
      var observer = new MutationObserver(function() {
        var newPills = document.querySelectorAll('.pill:not(.liquid-glass-ready)');
        newPills.forEach(function(pill) {
          pill.classList.add('liquid-glass-ready');
        });
      });
      observer.observe(document.body, { childList: true, subtree: true });
    }
  }

  function init() {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', createSVGFilter);
    } else {
      createSVGFilter();
    }
  }

  // Expose globally
  window.OMLLiquidGlass = {
    init: init,
    filterId: filterId
  };

  init();
})();
