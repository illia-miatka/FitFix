import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

// The two faces of DESIGN.md, self-hosted so the page needs no third-party font request. Only
// the weights the three type roles use: display 700, body 500, metric 800.
import '@fontsource/unbounded/700.css';
import '@fontsource/manrope/500.css';
import '@fontsource/manrope/800.css';

import './theme.css';
import { App } from './App';

const container = document.getElementById('root');
if (container === null) throw new Error('index.html is missing #root.');

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
