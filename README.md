"configurations": {
  "production": {
   ** "outputHashing": "none", // ← change from "all" to "none"**
    "optimization": true,
    "budgets": [
      {
        "type": "initial",
        "maximumWarning": "500kB",
        "maximumError": "1MB"
      },
      {
        "type": "anyComponentStyle",
        "maximumWarning": "4kB",
        "maximumError": "8kB"
      }
    ]
  },
  "development": {
    "outputHashing": "none", // ← change from "all" to "none"
    "optimization": false,
    "extractLicenses": false,
    "sourceMap": true
  }

  withHashLocation

in app.config.ts
import {
  provideRouter,
  withComponentInputBinding,
  withInMemoryScrolling,
  withHashLocation
} from '@angular/router';
  provideRouter(
  routes,
  withInMemoryScrolling({
    scrollPositionRestoration: 'enabled',
    anchorScrolling: 'enabled',
  }),
  withComponentInputBinding(),
  withHashLocation()
),
