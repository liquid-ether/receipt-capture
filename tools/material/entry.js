// The Material Web components the front end uses, and nothing else — each import
// registers one custom element, and everything not listed here stays out of the
// bundle.

import '@material/web/button/filled-button.js';
import '@material/web/button/filled-tonal-button.js';
import '@material/web/button/outlined-button.js';
import '@material/web/button/text-button.js';
import '@material/web/dialog/dialog.js';
import '@material/web/elevation/elevation.js';
import '@material/web/focus/md-focus-ring.js';
import '@material/web/icon/icon.js';
import '@material/web/iconbutton/icon-button.js';
import '@material/web/progress/circular-progress.js';
import '@material/web/ripple/ripple.js';
import '@material/web/select/outlined-select.js';
import '@material/web/select/select-option.js';
import '@material/web/switch/switch.js';
import '@material/web/textfield/outlined-text-field.js';

import { styles as typescaleStyles } from '@material/web/typography/md-typescale-styles.js';

// Makes the official type-scale classes (.md-typescale-title-large and friends)
// available to ordinary page markup, not just inside the components.
document.adoptedStyleSheets = [...document.adoptedStyleSheets, typescaleStyles.styleSheet];
