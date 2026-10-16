// Fonctions navigateur du socle commun (appelées depuis Blazor par window.barthazon.*).
window.barthazon = (function () {
    let surveillance = null;

    return {
        /** Vrai sur un écran tactile (téléphone, tablette) : « Appareil partagé » coché par défaut. */
        estTactile: function () {
            return window.matchMedia("(pointer: coarse)").matches;
        },

        /**
         * Suit l'activité de l'utilisateur :
         * - prolonge la session côté serveur tant qu'il est actif (cookie glissant) ;
         * - sur un appareil partagé, déconnecte après la durée d'inactivité.
         */
        surveillerActivite: function (partage, minutesInactivite) {
            if (surveillance) {
                return;
            }

            let derniere = Date.now();
            let dernierSignal = Date.now();
            const noter = function () { derniere = Date.now(); };
            ["pointerdown", "keydown", "wheel", "touchstart"].forEach(function (e) {
                document.addEventListener(e, noter, { passive: true, capture: true });
            });

            surveillance = window.setInterval(function () {
                const maintenant = Date.now();
                if (partage && maintenant - derniere > minutesInactivite * 60000) {
                    window.location.href = "/compte/deconnexion?motif=inactivite";
                    return;
                }

                if (derniere > dernierSignal && maintenant - dernierSignal > 60000) {
                    dernierSignal = maintenant;
                    fetch("/compte/actif", { credentials: "same-origin", cache: "no-store" }).then(function (r) {
                        if (r.status === 401) {
                            window.location.href = "/connexion";
                        }
                    }).catch(function () { /* réseau coupé : l'écran de reconnexion s'en charge */ });
                }
            }, 15000);
        },

        imprimer: function () {
            window.print();
        },

        /** Aperçu immédiat de la couleur principale (Paramètres → Apparence). */
        definirCouleur: function (couleur, texte) {
            document.documentElement.style.setProperty("--couleur-principale", couleur);
            document.documentElement.style.setProperty("--texte-sur-principale", texte);
        },

        /** Téléchargement d'un fichier produit côté serveur (export CSV). */
        telecharger: async function (nom, flux) {
            const tampon = await flux.arrayBuffer();
            const url = URL.createObjectURL(new Blob([tampon]));
            const lien = document.createElement("a");
            lien.href = url;
            lien.download = nom;
            document.body.appendChild(lien);
            lien.click();
            lien.remove();
            URL.revokeObjectURL(url);
        },

        focaliser: function (element) {
            if (element && element.focus) {
                element.focus();
            }
        },

        /** Clavier physique sur l'écran de connexion : chiffres, retour arrière, Entrée. */
        ecouterClavierPin: function (reference) {
            const gestionnaire = function (e) {
                if (/^[0-9]$/.test(e.key) || e.key === "Backspace" || e.key === "Enter" || e.key === "Escape") {
                    e.preventDefault();
                    reference.invokeMethodAsync("Touche", e.key);
                }
            };
            document.addEventListener("keydown", gestionnaire);
            return {
                arreter: function () { document.removeEventListener("keydown", gestionnaire); }
            };
        }
    };
})();
