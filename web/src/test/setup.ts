// Importing the /vitest entry point registers jest-dom's matchers with `expect` and augments
// its types, so `toBeDisabled` needs no declaration of its own anywhere.
import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

afterEach(cleanup);
