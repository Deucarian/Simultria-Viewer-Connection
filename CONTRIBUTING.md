# Contributing

Keep changes inside the Simultria-to-viewer adapter boundary. Backend routes and
environment URL templates belong to `com.deucarian.simultria-api`; generic
transport and command protocol behavior belong to their respective owner
packages. Add tests for public contract and safety changes, then run the shared
package validator and EditMode tests.
