# Terraform Registry frontend

This frontend uses npm. Keep `package-lock.json` in sync with `package.json`.

## Setup

Install the locked dependency graph:

```bash
npm ci
```

## Development Server

Start the development server on `http://localhost:3000`:

```bash
npm run dev
```

## Production

Build the application for production:

```bash
npm run build
```

Locally preview production build:

```bash
npm run preview
```

Check out the [deployment documentation](https://nuxt.com/docs/getting-started/deployment) for more information.
